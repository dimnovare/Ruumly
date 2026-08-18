using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Tests;

/// <summary>
/// GET /api/request-status/{token} — the concierge customer's own view of their
/// request, the thing that lets them tell a slow success from a silent failure.
///
/// The leak assertions here are deliberately blunt: every state is SERIALISED
/// and the resulting JSON is searched for supplier names, supplier emails,
/// prices and admin notes. A field-by-field check only proves the fields we
/// remembered to check; the whole risk of this endpoint is the field somebody
/// adds later without thinking about who is on the other end of the link.
/// </summary>
public class RequestStatusTests
{
    private const string Token = "status-token-abc";

    private static RequestStatusController MakePublic(RuumlyDbContext db) =>
        new(db)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static DemandLead MakeLead(
        RuumlyDbContext db,
        string? token = Token,
        DemandLeadStatus status = DemandLeadStatus.New)
    {
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(),
            Email = "cust@x.ee", Name = "Mari Maasikas", Phone = "+372 5555 1234",
            City = "Viljandi", ToCity = "Tartu", Category = DemandLeadCategory.Warehouse,
            NeedDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            Details = "10 m2 of furniture", Language = "et", Source = "concierge",
            AdminNotes = "SECRET-ADMIN-NOTE called them twice",
            Status = status, CreatedAt = DateTime.UtcNow.AddDays(-3),
            StatusToken = token,
        };
        db.DemandLeads.Add(lead);
        db.SaveChanges();
        return lead;
    }

    private static Supplier MakeSupplier(RuumlyDbContext db, string name, string email)
    {
        var s = new Supplier
        {
            Id = Guid.NewGuid(), Name = name, ContactName = "C",
            ContactEmail = email, ContactPhone = "1", IsActive = true,
        };
        db.Suppliers.Add(s);
        db.SaveChanges();
        return s;
    }

    private static ProviderOutreach MakeOutreach(
        RuumlyDbContext db, DemandLead lead, Supplier supplier,
        ProviderOutreachStatus status = ProviderOutreachStatus.Sent,
        DateTime? sentAt = null, decimal? quotedAmount = null)
    {
        var row = new ProviderOutreach
        {
            Id = Guid.NewGuid(), DemandLeadId = lead.Id, SupplierId = supplier.Id,
            SentTo = supplier.ContactEmail, SentAt = sentAt ?? DateTime.UtcNow.AddDays(-2),
            Status = status,
            QuotedAmount = quotedAmount,
            QuotedAt = quotedAmount is null ? null : DateTime.UtcNow.AddDays(-1),
            QuoteToken = "quote-token-" + Guid.NewGuid().ToString("N")[..8],
        };
        db.ProviderOutreaches.Add(row);
        db.SaveChanges();
        return row;
    }

    private static Offer MakeOffer(
        RuumlyDbContext db, DemandLead lead, OfferStatus status, string token,
        decimal price = 129m, Guid? supplierId = null)
    {
        var offer = new Offer
        {
            Id = Guid.NewGuid(), DemandLeadId = lead.Id, Token = token, Status = status,
            Language = "et", CreatedAt = DateTime.UtcNow.AddDays(-1),
            SentAt = status == OfferStatus.Draft ? null : DateTime.UtcNow.AddHours(-5),
            CustomerNote = "See on parim pakkumine",
        };
        db.Offers.Add(offer);
        db.OfferOptions.Add(new OfferOption
        {
            Id = Guid.NewGuid(), OfferId = offer.Id, SupplierId = supplierId,
            Title = "Viljandi Ladu OÜ — Viljandi", PriceAmount = price, PriceUnit = "kuus",
        });
        db.SaveChanges();
        return offer;
    }

    private static RequestStatusDto Ok(IActionResult result) =>
        result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<RequestStatusDto>().Subject;

    // ── Unknown token is indistinguishable from a missing one ─────────────────

    [Fact]
    public async Task UnknownToken_404s_IdenticallyToAMissingOne()
    {
        var db = TestDbContext.Create();
        // A real lead exists — so a difference in the two responses could only
        // come from leaking whether the token matched something.
        MakeLead(db);
        var api = MakePublic(db);

        var unknown = await api.GetStatus("no-such-token", CancellationToken.None);
        var missing = await api.GetStatus("", CancellationToken.None);
        var blank   = await api.GetStatus("   ", CancellationToken.None);

        var unknownBody = unknown.Should().BeOfType<NotFoundObjectResult>().Subject;
        var missingBody = missing.Should().BeOfType<NotFoundObjectResult>().Subject;
        var blankBody   = blank.Should().BeOfType<NotFoundObjectResult>().Subject;

        unknownBody.StatusCode.Should().Be(missingBody.StatusCode).And.Be(blankBody.StatusCode);
        JsonSerializer.Serialize(unknownBody.Value)
            .Should().Be(JsonSerializer.Serialize(missingBody.Value))
            .And.Be(JsonSerializer.Serialize(blankBody.Value));
    }

    [Fact]
    public async Task NullStatusToken_IsNotAddressable()
    {
        var db = TestDbContext.Create();
        // Every row created before this feature carries null. A null token must
        // not be reachable by sending nothing.
        MakeLead(db, token: null);

        (await MakePublic(db).GetStatus("", CancellationToken.None))
            .Should().BeOfType<NotFoundObjectResult>();
        (await MakePublic(db).GetStatus("null", CancellationToken.None))
            .Should().BeOfType<NotFoundObjectResult>();
    }

    // ── The request read back ─────────────────────────────────────────────────

    [Fact]
    public async Task ReadsTheRequestBack_SoTheCustomerCanSpotTheirOwnTypo()
    {
        var db   = TestDbContext.Create();
        var lead = MakeLead(db);
        // Real issued keys (LeadPhotoNormalizer re-validates on the way out, so
        // an invented shape silently counts as zero photos).
        lead.PhotoKeysJson =
            """["2026/08/lead-photos/0123456789abcdef0123456789abcdef.jpg","2026/08/lead-photos/fedcba9876543210fedcba9876543210.jpg"]""";
        db.SaveChanges();

        var dto = Ok(await MakePublic(db).GetStatus(Token, CancellationToken.None));

        dto.Request.Service.Should().Be("warehouse");
        dto.Request.City.Should().Be("Viljandi");
        dto.Request.ToCity.Should().Be("Tartu");
        dto.Request.NeedDate.Should().Be(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        dto.Request.Details.Should().Be("10 m2 of furniture");
        dto.Request.PhotoCount.Should().Be(2);
        dto.Request.SubmittedAt.Should().Be(lead.CreatedAt);
        dto.State.Should().Be("received");
        dto.Closed.Should().BeFalse();
    }

    [Fact]
    public async Task NeverEchoesTheStreetAddress()
    {
        var db   = TestDbContext.Create();
        var lead = MakeLead(db);
        lead.FromAddress = "Tallinna mnt 12-4";
        lead.ToAddress   = "Riia 8";
        db.SaveChanges();

        var json = JsonSerializer.Serialize(Ok(await MakePublic(db).GetStatus(Token, CancellationToken.None)));

        json.Should().NotContain("Tallinna mnt 12-4").And.NotContain("Riia 8",
            "the credential is a link in an email, and emails get forwarded");
    }

    // ── The contacted count is a real number, and the honest one ──────────────

    [Fact]
    public async Task ProvidersContacted_CountsEveryProviderThatActuallyGotIt()
    {
        var db   = TestDbContext.Create();
        var lead = MakeLead(db);
        var first = DateTime.UtcNow.AddDays(-4);

        MakeOutreach(db, lead, MakeSupplier(db, "Alpha OÜ", "a@x.ee"),
            ProviderOutreachStatus.Sent, sentAt: first.AddHours(2));
        MakeOutreach(db, lead, MakeSupplier(db, "Beta OÜ", "b@x.ee"),
            ProviderOutreachStatus.Sent, sentAt: first);
        MakeOutreach(db, lead, MakeSupplier(db, "Gamma OÜ", "c@x.ee"),
            ProviderOutreachStatus.Declined, sentAt: first.AddHours(3));
        MakeOutreach(db, lead, MakeSupplier(db, "Delta OÜ", "d@x.ee"),
            ProviderOutreachStatus.NoAnswer, sentAt: first.AddHours(4));
        // Arrived and was read enough to be judged — that is contact.
        MakeOutreach(db, lead, MakeSupplier(db, "Epsilon OÜ", "e@x.ee"),
            ProviderOutreachStatus.Complained, sentAt: first.AddHours(5));
        // Never reached a human. Must NOT be counted.
        MakeOutreach(db, lead, MakeSupplier(db, "Zeta OÜ", "f@x.ee"),
            ProviderOutreachStatus.Bounced, sentAt: first.AddHours(6));

        var dto = Ok(await MakePublic(db).GetStatus(Token, CancellationToken.None));

        dto.ProvidersContacted.Should().Be(5, "six went out, one bounced");
        dto.ProvidersContactedAt.Should().Be(first, "the earliest one that actually left");
        dto.State.Should().Be("contacted");
    }

    [Fact]
    public async Task ProvidersContacted_IsZeroWithNoOutreach_AndCarriesNoDate()
    {
        var db = TestDbContext.Create();
        MakeLead(db);

        var dto = Ok(await MakePublic(db).GetStatus(Token, CancellationToken.None));

        dto.ProvidersContacted.Should().Be(0);
        dto.ProvidersContactedAt.Should().BeNull();
        dto.State.Should().Be("received");
    }

    [Fact]
    public async Task ProvidersContacted_ExcludesOutreachForOtherRequests()
    {
        var db    = TestDbContext.Create();
        var mine  = MakeLead(db);
        var other = MakeLead(db, token: "someone-elses-token");

        MakeOutreach(db, mine,  MakeSupplier(db, "Alpha OÜ", "a@x.ee"));
        MakeOutreach(db, other, MakeSupplier(db, "Beta OÜ",  "b@x.ee"));
        MakeOutreach(db, other, MakeSupplier(db, "Gamma OÜ", "c@x.ee"));

        Ok(await MakePublic(db).GetStatus(Token, CancellationToken.None))
            .ProvidersContacted.Should().Be(1);
    }

    // ── States ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AQuoteBack_MovesToCollecting_WithoutRevealingThePrice()
    {
        var db   = TestDbContext.Create();
        var lead = MakeLead(db, status: DemandLeadStatus.Contacted);
        MakeOutreach(db, lead, MakeSupplier(db, "Alpha OÜ", "a@x.ee"));
        MakeOutreach(db, lead, MakeSupplier(db, "Beta OÜ", "b@x.ee"),
            ProviderOutreachStatus.Replied, quotedAmount: 87.50m);

        var dto = Ok(await MakePublic(db).GetStatus(Token, CancellationToken.None));

        dto.State.Should().Be("collecting");
        dto.OfferSent.Should().BeFalse();
        dto.OfferToken.Should().BeNull();
        JsonSerializer.Serialize(dto).Should().NotContain("87.5").And.NotContain("87,5");
    }

    [Fact]
    public async Task ADraftOffer_IsNotAnnounced_BecauseReleasingIsAHumanDecision()
    {
        var db   = TestDbContext.Create();
        var lead = MakeLead(db, status: DemandLeadStatus.Contacted);
        MakeOutreach(db, lead, MakeSupplier(db, "Alpha OÜ", "a@x.ee"),
            ProviderOutreachStatus.Replied, quotedAmount: 200m);
        MakeOffer(db, lead, OfferStatus.Draft, "draft-offer-token");

        var dto = Ok(await MakePublic(db).GetStatus(Token, CancellationToken.None));

        dto.State.Should().Be("collecting");
        dto.OfferSent.Should().BeFalse();
        dto.OfferToken.Should().BeNull();
        JsonSerializer.Serialize(dto).Should().NotContain("draft-offer-token");
    }

    [Fact]
    public async Task ASentOffer_LinksToTheExistingOfferPage()
    {
        var db    = TestDbContext.Create();
        var lead  = MakeLead(db, status: DemandLeadStatus.Quoted);
        MakeOutreach(db, lead, MakeSupplier(db, "Alpha OÜ", "a@x.ee"),
            ProviderOutreachStatus.Replied, quotedAmount: 129m);
        var offer = MakeOffer(db, lead, OfferStatus.Sent, "live-offer-token");

        var dto = Ok(await MakePublic(db).GetStatus(Token, CancellationToken.None));

        dto.State.Should().Be("offer_sent");
        dto.OfferSent.Should().BeTrue();
        dto.OfferToken.Should().Be("live-offer-token");
        dto.OfferSentAt.Should().Be(offer.SentAt);
        dto.Closed.Should().BeFalse();
    }

    [Fact]
    public async Task AnExpiredOffer_IsNeverLinked_BecauseTheOfferPage404s()
    {
        var db   = TestDbContext.Create();
        var lead = MakeLead(db, status: DemandLeadStatus.Contacted);
        MakeOutreach(db, lead, MakeSupplier(db, "Alpha OÜ", "a@x.ee"));
        MakeOffer(db, lead, OfferStatus.Expired, "expired-offer-token");

        var dto = Ok(await MakePublic(db).GetStatus(Token, CancellationToken.None));

        dto.OfferSent.Should().BeFalse();
        dto.OfferToken.Should().BeNull();
        dto.State.Should().Be("contacted");
    }

    [Fact]
    public async Task AChosenOffer_WinsOverANewerLiveOne()
    {
        var db   = TestDbContext.Create();
        var lead = MakeLead(db, status: DemandLeadStatus.Quoted);
        MakeOffer(db, lead, OfferStatus.Chosen, "chosen-offer-token");
        var newer = MakeOffer(db, lead, OfferStatus.Sent, "newer-offer-token");
        newer.SentAt = DateTime.UtcNow;
        db.SaveChanges();

        var dto = Ok(await MakePublic(db).GetStatus(Token, CancellationToken.None));

        dto.State.Should().Be("chosen");
        dto.OfferToken.Should().Be("chosen-offer-token");
    }

    // ── Terminal states ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(DemandLeadStatus.Converted, "booked")]
    [InlineData(DemandLeadStatus.Unmatched, "no_match")]
    [InlineData(DemandLeadStatus.Dismissed, "closed")]
    public async Task AClosedRequest_RendersATerminalState(DemandLeadStatus status, string expected)
    {
        var db   = TestDbContext.Create();
        var lead = MakeLead(db, status: status);
        MakeOutreach(db, lead, MakeSupplier(db, "Alpha OÜ", "a@x.ee"));

        var dto = Ok(await MakePublic(db).GetStatus(Token, CancellationToken.None));

        dto.State.Should().Be(expected);
        dto.Closed.Should().BeTrue("nothing further will happen on its own");
        dto.ProvidersContacted.Should().Be(1, "what we did still happened");
    }

    [Fact]
    public async Task AnUnmatchedRequest_SaysSo_EvenAfterEighteenSilentProviders()
    {
        // The 2026-08-18 Viljandi shape: many providers contacted, nobody replied.
        var db   = TestDbContext.Create();
        var lead = MakeLead(db, status: DemandLeadStatus.Unmatched);
        for (var i = 0; i < 18; i++)
            MakeOutreach(db, lead, MakeSupplier(db, $"Provider {i} OÜ", $"p{i}@x.ee"));

        var dto = Ok(await MakePublic(db).GetStatus(Token, CancellationToken.None));

        dto.State.Should().Be("no_match");
        dto.ProvidersContacted.Should().Be(18);
        dto.OfferSent.Should().BeFalse();
    }

    [Fact]
    public async Task StateNeverEchoesTheInternalLeadStatusName()
    {
        var db = TestDbContext.Create();
        MakeLead(db, status: DemandLeadStatus.Unmatched);

        var json = JsonSerializer.Serialize(Ok(await MakePublic(db).GetStatus(Token, CancellationToken.None)));

        json.Should().NotContain("Unmatched").And.NotContain("Dismissed").And.NotContain("Converted");
    }

    // ── The whole point: serialise every state and prove nothing leaks ────────

    [Theory]
    [InlineData(DemandLeadStatus.New)]
    [InlineData(DemandLeadStatus.Contacted)]
    [InlineData(DemandLeadStatus.Quoted)]
    [InlineData(DemandLeadStatus.Converted)]
    [InlineData(DemandLeadStatus.Dismissed)]
    [InlineData(DemandLeadStatus.Unmatched)]
    public async Task NoStateEverLeaksAProviderIdentity_APrice_OrAnAdminNote(DemandLeadStatus status)
    {
        var db   = TestDbContext.Create();
        var lead = MakeLead(db, status: status);
        var alpha = MakeSupplier(db, "Viljandi Ladu OÜ", "ladu@viljandi.ee");
        var beta  = MakeSupplier(db, "Suur Kolimine OÜ", "info@kolimine.ee");

        MakeOutreach(db, lead, alpha, ProviderOutreachStatus.Replied, quotedAmount: 64.25m);
        MakeOutreach(db, lead, beta,  ProviderOutreachStatus.Bounced);
        // One of each offer state, so whichever the controller picks is covered.
        MakeOffer(db, lead, OfferStatus.Draft, "draft-tok", price: 111.11m, supplierId: alpha.Id);
        MakeOffer(db, lead, OfferStatus.Sent,  "sent-tok",  price: 222.22m, supplierId: beta.Id);

        var json = JsonSerializer.Serialize(Ok(await MakePublic(db).GetStatus(Token, CancellationToken.None)));

        // Provider identity — the concierge model is that WE broker the intro.
        json.Should().NotContain("Viljandi Ladu").And.NotContain("Suur Kolimine");
        json.Should().NotContain("ladu@viljandi.ee").And.NotContain("info@kolimine.ee");
        // Prices — releasing an offer is a human decision this must not pre-empt.
        json.Should().NotContain("64.25").And.NotContain("111.11").And.NotContain("222.22");
        json.Should().NotContain("kuus", "a price unit is half a price");
        // Internal notes and the customer's own contact details.
        json.Should().NotContain("SECRET-ADMIN-NOTE");
        json.Should().NotContain("cust@x.ee").And.NotContain("+372 5555 1234");
        json.Should().NotContain("See on parim pakkumine", "the offer's own copy belongs on the offer page");
        // The status token is the credential — never echo the credential.
        json.Should().NotContain(Token);
        // A draft is ops working; it is not a promise to anybody.
        json.Should().NotContain("draft-tok");
    }
}
