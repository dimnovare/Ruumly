using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests.Integration;

/// <summary>
/// The provider quote submit against the REAL Npgsql provider. The unit tests
/// run on EF InMemory, which takes the non-relational branch — this exercises
/// the branch that actually runs in production: the serializable transaction and
/// the FOR UPDATE raw SQL that locks the outreach row and its lead (a typo in
/// that SQL would only ever surface as a 500 in prod).
/// </summary>
[Collection("Postgres integration")]
public class QuoteSubmitNpgsqlTests(PostgresIntegrationFixture pg)
{
    private sealed class CapturingEmailQueue : IBackgroundEmailQueue
    {
        public List<string> Recipients { get; } = [];

        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null)
            => Recipients.Add(to);

        public void EnqueueEmail(
            string to, string subject, string textBody, string? htmlBody, string? replyTo)
            => Recipients.Add(to);

        public void EnqueueVerificationEmail(Guid userId) { }
    }

    private static QuoteController MakePublic(RuumlyDbContext db, IBackgroundEmailQueue queue) =>
        new(db, queue)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private const string Unavailable =
        "PostgreSQL integration database unavailable. Start local PostgreSQL on port 5433 " +
        "or set RUUMLY_TEST_PG before running this focused gate.";

    [Fact]
    public async Task SubmitQuote_OnNpgsql_MarksReplied_SeedsDraftOptionOnce_AndIsIdempotent()
    {
        Assert.True(pg.Available, Unavailable);

        var leadId     = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var token      = OfferToken.Generate();

        await using (var seed = pg.NewContext())
        {
            seed.DemandLeads.Add(new DemandLead
            {
                Id = leadId, Email = "customer@x.ee", City = "Tallinn", ToCity = "Tartu",
                Category = DemandLeadCategory.Moving, Language = "en", Source = "concierge",
                Status = DemandLeadStatus.Contacted, ContactedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            });
            seed.Suppliers.Add(new Supplier
            {
                Id = supplierId, Name = "Npgsql Provider", RegistryCode = Guid.NewGuid().ToString("N"),
                ContactName = "Provider", ContactEmail = $"provider-{Guid.NewGuid():N}@x.ee",
                ContactPhone = "1", IsActive = true,
            });
            seed.ProviderOutreaches.Add(new ProviderOutreach
            {
                Id = Guid.NewGuid(), DemandLeadId = leadId, SupplierId = supplierId,
                SentTo = "provider@x.ee", SentAt = DateTime.UtcNow,
                Status = ProviderOutreachStatus.Sent, QuoteToken = token,
            });
            await seed.SaveChangesAsync();
        }

        // First submit — a fresh context, exactly like a real request.
        await using (var db = pg.NewContext())
        {
            (await MakePublic(db, new CapturingEmailQueue())
                    .SubmitQuote(token, new SubmitQuoteRequest(250m, "onetime", "next week", "2 movers")))
                .Should().BeOfType<OkObjectResult>();
        }

        await using (var verify = pg.NewContext())
        {
            var row = await verify.ProviderOutreaches.SingleAsync(o => o.QuoteToken == token);
            row.Status.Should().Be(ProviderOutreachStatus.Replied);
            row.QuotedAmount.Should().Be(250m);
            row.QuotedUnit.Should().Be("onetime");
            row.QuotedAt.Should().NotBeNull();

            var offer = await verify.Offers.Include(o => o.Options)
                .SingleAsync(o => o.DemandLeadId == leadId);
            offer.Status.Should().Be(OfferStatus.Draft);
            offer.Options.Should().ContainSingle().Which.SupplierId.Should().Be(supplierId);
        }

        // Re-submit on another fresh context — updates the same option, never duplicates.
        await using (var db = pg.NewContext())
        {
            (await MakePublic(db, new CapturingEmailQueue())
                    .SubmitQuote(token, new SubmitQuoteRequest(199m, "onetime", "this week", "revised")))
                .Should().BeOfType<OkObjectResult>();
        }

        await using (var verify = pg.NewContext())
        {
            (await verify.Offers.CountAsync(o => o.DemandLeadId == leadId))
                .Should().Be(1, "re-submit reuses the newest draft offer");
            var offer = await verify.Offers.Include(o => o.Options)
                .SingleAsync(o => o.DemandLeadId == leadId);
            offer.Options.Should().ContainSingle("the SupplierId-keyed option is updated, not duplicated")
                .Which.PriceAmount.Should().Be(199m);
            (await verify.ProviderOutreaches.CountAsync(o => o.QuoteToken == token)).Should().Be(1);
            (await verify.Bookings.CountAsync()).Should().Be(0);
            (await verify.Orders.CountAsync()).Should().Be(0);
        }
    }

    [Fact]
    public async Task SubmitQuote_OnNpgsql_UnknownToken_404()
    {
        Assert.True(pg.Available, Unavailable);

        await using var db = pg.NewContext();
        (await MakePublic(db, new CapturingEmailQueue())
                .SubmitQuote("does-not-exist", new SubmitQuoteRequest(10m)))
            .Should().BeOfType<NotFoundObjectResult>();
    }
}
