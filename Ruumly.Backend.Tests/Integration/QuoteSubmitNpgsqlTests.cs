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
        // The REAL auto-send service, with no offerAutoSend setting row in this
        // database — so every existing expectation in this file now also proves
        // that a provider quote does not email the customer by default.
        new(db, queue,
            new Ruumly.Backend.Services.Implementations.OfferAutoSendService(
                db, queue,
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<
                    Ruumly.Backend.Services.Implementations.OfferAutoSendService>.Instance),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<QuoteController>.Instance)
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

    /// <summary>
    /// The branch that actually broke: attaching an option to an offer that is
    /// ALREADY tracked (a pre-existing draft, or the draft the first provider's
    /// quote just created). Adding via the nav collection tracked the option as
    /// Modified, so SaveChanges UPDATEd a nonexistent row and the throw rolled
    /// the whole serializable transaction back — destroying the provider's quote.
    /// Covers both an admin-pre-built draft and a second provider on one lead.
    /// </summary>
    [Fact]
    public async Task SubmitQuote_OnNpgsql_IntoPreExistingDraft_AndFromASecondProvider_LosesNoQuote()
    {
        Assert.True(pg.Available, Unavailable);

        var leadId  = Guid.NewGuid();
        var offerId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var firstToken  = OfferToken.Generate();
        var secondToken = OfferToken.Generate();

        await using (var seed = pg.NewContext())
        {
            seed.DemandLeads.Add(new DemandLead
            {
                Id = leadId, Email = "customer@x.ee", City = "Tallinn",
                Category = DemandLeadCategory.Moving, Language = "en", Source = "concierge",
                Status = DemandLeadStatus.Contacted, ContactedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            });
            foreach (var (id, token) in new[] { (firstId, firstToken), (secondId, secondToken) })
            {
                seed.Suppliers.Add(new Supplier
                {
                    Id = id, Name = $"Provider {id:N}"[..20], RegistryCode = Guid.NewGuid().ToString("N"),
                    ContactName = "P", ContactEmail = $"p-{Guid.NewGuid():N}@x.ee",
                    ContactPhone = "1", IsActive = true,
                });
                seed.ProviderOutreaches.Add(new ProviderOutreach
                {
                    Id = Guid.NewGuid(), DemandLeadId = leadId, SupplierId = id,
                    SentTo = "p@x.ee", SentAt = DateTime.UtcNow,
                    Status = ProviderOutreachStatus.Sent, QuoteToken = token,
                });
            }
            // The admin pre-built the draft (the Stage-2 flow) — so the FIRST
            // quote already has to attach to a tracked, existing offer.
            seed.Offers.Add(new Offer
            {
                Id = offerId, DemandLeadId = leadId, Token = OfferToken.Generate(),
                Status = OfferStatus.Draft, Language = "en", CreatedAt = DateTime.UtcNow,
                CreatedBy = "ops@ruumly.eu",
                Options = { new OfferOption
                {
                    Id = Guid.NewGuid(), OfferId = offerId, Title = "Admin's own option",
                    PriceAmount = 500m, SortOrder = 0,
                } },
            });
            await seed.SaveChangesAsync();
        }

        foreach (var (token, amount) in new[] { (firstToken, 250m), (secondToken, 300m) })
        {
            await using var db = pg.NewContext();
            (await MakePublic(db, new CapturingEmailQueue())
                    .SubmitQuote(token, new SubmitQuoteRequest(amount, "onetime")))
                .Should().BeOfType<OkObjectResult>($"the quote for {amount} must not be lost");
        }

        await using (var verify = pg.NewContext())
        {
            (await verify.ProviderOutreaches.CountAsync(o =>
                    o.DemandLeadId == leadId && o.Status == ProviderOutreachStatus.Replied))
                .Should().Be(2, "both providers' quotes survived");

            (await verify.Offers.CountAsync(o => o.DemandLeadId == leadId))
                .Should().Be(1, "the pre-existing draft is reused, never duplicated");
            var offer = await verify.Offers.Include(o => o.Options).SingleAsync(o => o.Id == offerId);
            offer.Options.Should().HaveCount(3, "the admin's option plus one per provider, to compare");
            offer.Options.Where(o => o.CreatedFromOutreachId != null)
                .Select(o => o.PriceAmount).Should().BeEquivalentTo([250m, 300m]);
            offer.Options.Single(o => o.CreatedFromOutreachId == null)
                .Title.Should().Be("Admin's own option");
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
