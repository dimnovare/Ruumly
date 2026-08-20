using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests.Integration;

/// <summary>
/// The exactly-once guarantee for provider outcome letters, asserted against
/// real PostgreSQL — which is the only place it exists.
///
/// The in-memory `ProviderNotifiedSentAt is not null` check inside the notifier
/// is ADVISORY: it reads tracked entity state, and the letters are handed to
/// Hangfire (which commits on its own connection) before the pass saves. What
/// actually makes the send exactly-once is a conditional
/// <c>UPDATE ... WHERE marker IS NULL</c>, and the InMemory provider cannot run
/// one. A unit test would therefore have exercised the fallback and proved
/// nothing about production, so the property is asserted here instead.
///
/// This matters because the race is reachable rather than theoretical:
/// ConfirmBooking releases its <c>FOR UPDATE</c> lock at the commit immediately
/// before the notifier runs, so a second request is let go at exactly the moment
/// the first one begins its unguarded pass. A double-clicked confirm would
/// otherwise send every losing provider two "the customer chose another
/// provider" letters.
/// </summary>
[Collection("Postgres integration")]
public class ProviderOutcomeNotifierNpgsqlTests(PostgresIntegrationFixture pg)
{
    private sealed class CapturingEmailQueue : IBackgroundEmailQueue
    {
        private readonly object _gate = new();
        public List<string> Recipients { get; } = [];

        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null)
        {
            lock (_gate) Recipients.Add(to);
        }

        public void EnqueueEmail(
            string to, string subject, string textBody, string? htmlBody, string? replyTo)
        {
            lock (_gate) Recipients.Add(to);
        }

        public void EnqueueVerificationEmail(Guid userId) { }
    }

    private static ProviderOutcomeNotifier Make(RuumlyDbContext db, IBackgroundEmailQueue queue) =>
        new(db, queue, NullLogger<ProviderOutcomeNotifier>.Instance);

    private const string Unavailable =
        "PostgreSQL integration database unavailable. Start local PostgreSQL on port 5433 " +
        "or set RUUMLY_TEST_PG before running this focused gate.";

    /// <summary>A lead + Sent offer with one quote-seeded option per supplier.</summary>
    private async Task<Guid> SeedAsync(params (string Name, string Email)[] providers)
    {
        var leadId = Guid.NewGuid();
        var offerId = Guid.NewGuid();

        await using var seed = pg.NewContext();
        seed.DemandLeads.Add(new DemandLead
        {
            Id = leadId, Email = "customer@x.ee", City = "Tukums", ToCity = "Riga",
            Category = DemandLeadCategory.Moving, Language = "lv", Source = "concierge",
            Status = DemandLeadStatus.Quoted, ContactedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        });
        seed.Offers.Add(new Offer
        {
            Id = offerId, DemandLeadId = leadId, Token = OfferToken.Generate(),
            Status = OfferStatus.Sent, SentAt = DateTime.UtcNow, Language = "lv",
            CreatedAt = DateTime.UtcNow,
        });

        var order = 0;
        foreach (var (name, email) in providers)
        {
            var supplierId = Guid.NewGuid();
            seed.Suppliers.Add(new Supplier
            {
                Id = supplierId, Name = name, RegistryCode = Guid.NewGuid().ToString("N"),
                ContactName = "C", ContactEmail = email, ContactPhone = "1",
                IsActive = true, Country = "LV",
            });
            seed.OfferOptions.Add(new OfferOption
            {
                Id = Guid.NewGuid(), OfferId = offerId, SupplierId = supplierId,
                Title = $"{name} — Tukums", PriceAmount = 150m + order,
                PriceUnit = "vienreizējs", SortOrder = order++,
                CreatedFromOutreachId = Guid.NewGuid(),
            });
        }

        await seed.SaveChangesAsync();
        return offerId;
    }

    [Fact]
    public async Task ConcurrentOfferSentPasses_SendExactlyOneLetterPerProvider()
    {
        Assert.True(pg.Available, Unavailable);

        var offerId = await SeedAsync(
            ("Komanda24", $"a-{Guid.NewGuid():N}@x.lv"),
            ("JK Movers", $"b-{Guid.NewGuid():N}@x.lv"));

        // Two independent contexts = two independent requests, exactly as a
        // double-clicked send arrives.
        await using var db1 = pg.NewContext();
        await using var db2 = pg.NewContext();
        var queue = new CapturingEmailQueue();

        await Task.WhenAll(
            Make(db1, queue).NotifyOfferSentAsync(offerId),
            Make(db2, queue).NotifyOfferSentAsync(offerId));

        queue.Recipients.Should().HaveCount(2,
            "two providers, one letter each — however many passes race");
        queue.Recipients.Should().OnlyHaveUniqueItems();

        await using var verify = pg.NewContext();
        (await verify.OfferOptions.CountAsync(o =>
                o.OfferId == offerId && o.ProviderNotifiedSentAt == null))
            .Should().Be(0, "every option is claimed exactly once");
    }

    [Fact]
    public async Task ConcurrentOutcomePasses_SendExactlyOneLoserLetter()
    {
        Assert.True(pg.Available, Unavailable);

        var offerId = await SeedAsync(
            ("Winner", $"win-{Guid.NewGuid():N}@x.lv"),
            ("Loser",  $"lose-{Guid.NewGuid():N}@x.lv"));

        await using (var pick = pg.NewContext())
        {
            var offer = await pick.Offers.FirstAsync(o => o.Id == offerId);
            var winning = await pick.OfferOptions
                .Where(o => o.OfferId == offerId).OrderBy(o => o.SortOrder).FirstAsync();
            offer.Status = OfferStatus.Chosen;
            offer.ChosenOptionId = winning.Id;
            offer.ChosenAt = DateTime.UtcNow;
            await pick.SaveChangesAsync();
        }

        await using var db1 = pg.NewContext();
        await using var db2 = pg.NewContext();
        var queue = new CapturingEmailQueue();

        // A double-clicked confirm-booking.
        await Task.WhenAll(
            Make(db1, queue).NotifyOutcomeAsync(offerId, OutcomeAudience.LosersOnly),
            Make(db2, queue).NotifyOutcomeAsync(offerId, OutcomeAudience.LosersOnly));

        queue.Recipients.Should().ContainSingle(
            "one losing provider must never be told twice that they lost");
    }

    [Fact]
    public async Task ARepeatedSend_IsANoOp_OnRealPostgres()
    {
        Assert.True(pg.Available, Unavailable);

        var offerId = await SeedAsync(("Solo", $"solo-{Guid.NewGuid():N}@x.lv"));
        var queue = new CapturingEmailQueue();

        await using (var db = pg.NewContext())
            await Make(db, queue).NotifyOfferSentAsync(offerId);
        await using (var db = pg.NewContext())
            await Make(db, queue).NotifyOfferSentAsync(offerId);

        queue.Recipients.Should().ContainSingle(
            "POST /admin/offers/{id}/send is repeatable; the announcement is not");
    }
}
