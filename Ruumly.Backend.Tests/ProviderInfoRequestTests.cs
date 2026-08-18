using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Constants;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// The second action on the tokenized quote page: a provider who cannot price
/// the request says what is missing, instead of replying to a shared ops inbox
/// with free text that names no lead.
///
/// Written against the reply that made it necessary — Adduco, 2026-08-17,
/// "selle info pealt adekvaatset pakkumist paraku ei saa teha", needing to know
/// whether both ends were the same address and wanting photos. The properties
/// that matter: the ask is recorded and countable, the outreach stops reading as
/// silence, ops can tell WHICH lead it is about, the customer is not emailed,
/// and no response ever names the customer.
/// </summary>
public class ProviderInfoRequestTests
{
    private sealed class CapturingEmailQueue : IBackgroundEmailQueue
    {
        public List<(string To, string Subject, string TextBody)> Emails { get; } = [];
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null)
            => Emails.Add((to, subject, textBody));
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody, string? replyTo)
            => Emails.Add((to, subject, textBody));
        public void EnqueueVerificationEmail(Guid userId) { }
    }

    private static QuoteController MakePublic(RuumlyDbContext db, IBackgroundEmailQueue queue) =>
        new(db, queue,
            new Ruumly.Backend.Services.Implementations.OfferAutoSendService(
                db, queue,
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<
                    Ruumly.Backend.Services.Implementations.OfferAutoSendService>.Instance),
            new TestServices.NoStorage(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<QuoteController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private const string CustomerEmail = "cust@x.ee";
    private const string CustomerName  = "Mari Maasikas";
    private const string CustomerPhone = "+372 5555 1234";

    /// <summary>
    /// A lead, a supplier and a SENT outreach carrying a quote token — the state a
    /// provider is in when they open the link. Built directly rather than through
    /// the admin send, so these tests fail for their own reasons and not because
    /// the outreach batch changed.
    /// </summary>
    private static (DemandLead Lead, Supplier Supplier, ProviderOutreach Outreach, string Token)
        Seed(RuumlyDbContext db)
    {
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = CustomerEmail, Name = CustomerName, Phone = CustomerPhone,
            City = "Tallinn", ToCity = "Haapsalu", Category = DemandLeadCategory.Moving,
            NeedDate = new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc),
            Details = "1-room flat, one piano", Language = "et", Source = "concierge",
            Status = DemandLeadStatus.Contacted, ContactedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = "Adduco OÜ", ContactName = "C",
            ContactEmail = "info@adduco.ee", ContactPhone = "1", IsActive = true, Country = "EE",
        };
        var token = OfferToken.Generate();
        var outreach = new ProviderOutreach
        {
            Id = Guid.NewGuid(), DemandLeadId = lead.Id, SupplierId = supplier.Id,
            SentTo = supplier.ContactEmail, SentAt = DateTime.UtcNow,
            Status = ProviderOutreachStatus.Sent, QuoteToken = token,
        };

        db.DemandLeads.Add(lead);
        db.Suppliers.Add(supplier);
        db.ProviderOutreaches.Add(outreach);
        db.SaveChanges();
        return (lead, supplier, outreach, token);
    }

    private static object? Prop(object o, string name) =>
        o.GetType().GetProperty(name)!.GetValue(o);

    /// <summary>Everything an action result would actually send back over the wire.</summary>
    private static string BodyJson(IActionResult result) =>
        JsonSerializer.Serialize(((ObjectResult)result).Value);

    // ─── The happy path: recorded, no longer silent, ops knows which lead ─────

    [Fact]
    public async Task NeedInfo_RecordsTheAsk_FlipsOutreachOffSilence_AndAlertsOpsWithTheLeadReference()
    {
        var db = TestDbContext.Create();
        var (lead, supplier, outreach, token) = Seed(db);
        var ops = new CapturingEmailQueue();

        var result = await MakePublic(db, ops).NeedInfo(token, new NeedInfoRequest(
            Reasons: [InfoRequestReasons.Address, InfoRequestReasons.Photos],
            Note: "Kas peale- ja mahalaadimine on samal aadressil?"));

        var dto = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<NeedInfoSubmittedDto>().Subject;
        dto.Ok.Should().BeTrue();
        dto.Reasons.Should().BeEquivalentTo(["address", "photos"]);

        var row = db.ProviderInfoRequests.Should().ContainSingle().Subject;
        row.DemandLeadId.Should().Be(lead.Id);
        row.SupplierId.Should().Be(supplier.Id);
        row.ProviderOutreachId.Should().Be(outreach.Id,
            "which token was used is what tells ops who to answer");
        row.ResolvedAt.Should().BeNull("a fresh ask is open until ops closes it");
        row.Note.Should().Be("Kas peale- ja mahalaadimine on samal aadressil?");
        InfoRequestReasons.Parse(row.ReasonsJson).Should().BeEquivalentTo(["address", "photos"]);

        db.ProviderOutreaches.Single().Status.Should().Be(ProviderOutreachStatus.NeedsInfo,
            "the provider answered — reporting this outreach as silence is the bug");

        var alert = ops.Emails.Should().ContainSingle().Subject;
        alert.To.Should().Be(OpsInbox.Fallback);
        alert.Subject.Should().Contain(ProviderOutreachComposer.Reference(lead.Id),
            "the shared ops inbox cannot tell two Tallinn moves apart without the handle");
        alert.Subject.Should().Contain("Tallinn").And.Contain("Haapsalu");
        alert.TextBody.Should().Contain("Adduco OÜ")
            .And.Contain("Exact addresses", "the ops alert spells the reasons out")
            .And.Contain("Kas peale- ja mahalaadimine");
    }

    [Fact]
    public async Task NeedInfo_NeverEmailsTheCustomer_AndCreatesNoQuoteOfferOrCommerce()
    {
        var db = TestDbContext.Create();
        var (_, _, _, token) = Seed(db);
        var ops = new CapturingEmailQueue();

        (await MakePublic(db, ops).NeedInfo(token, new NeedInfoRequest([InfoRequestReasons.Photos])))
            .Should().BeOfType<OkObjectResult>();

        ops.Emails.Should().NotContain(e => e.To == CustomerEmail,
            "what to ask a customer, and in which language, is still a human decision");
        ops.Emails.Should().OnlyContain(e => e.To == OpsInbox.Fallback);

        var row = db.ProviderOutreaches.Single();
        row.QuotedAt.Should().BeNull("asking a question is not submitting a price");
        row.QuotedAmount.Should().BeNull();
        db.Offers.Should().BeEmpty("a blocked provider seeds no draft offer");
        db.OfferOptions.Should().BeEmpty();
        db.Bookings.Should().BeEmpty();
        db.Orders.Should().BeEmpty();
    }

    // ─── Token / lifecycle contract, identical to the price submit ────────────

    [Fact]
    public async Task NeedInfo_UnknownOrEmptyToken_404_AndLooksLikeAMissingOne()
    {
        var db = TestDbContext.Create();
        Seed(db);
        var ops = new CapturingEmailQueue();
        var pub = MakePublic(db, ops);

        var unknown = await pub.NeedInfo("does-not-exist", new NeedInfoRequest([InfoRequestReasons.Photos]));
        var blank   = await pub.NeedInfo("   ", new NeedInfoRequest([InfoRequestReasons.Photos]));

        unknown.Should().BeOfType<NotFoundObjectResult>();
        blank.Should().BeOfType<NotFoundObjectResult>();
        BodyJson(blank).Should().Be(BodyJson(unknown),
            "an unknown token must be indistinguishable from a missing one");

        db.ProviderInfoRequests.Should().BeEmpty();
        ops.Emails.Should().BeEmpty();
    }

    [Fact]
    public async Task NeedInfo_OnClosedLead_409_LeadClosed_AndRecordsNothing()
    {
        foreach (var terminal in new[]
        {
            DemandLeadStatus.Converted, DemandLeadStatus.Dismissed, DemandLeadStatus.Unmatched,
        })
        {
            var db = TestDbContext.Create();
            var (lead, _, _, token) = Seed(db);
            var ops = new CapturingEmailQueue();

            lead.Status = terminal;
            await db.SaveChangesAsync();

            var conflict = (await MakePublic(db, ops).NeedInfo(
                    token, new NeedInfoRequest([InfoRequestReasons.Photos], "still need pictures")))
                .Should().BeOfType<ConflictObjectResult>($"{terminal} is a closed request").Subject;
            Prop(conflict.Value!, "reason").Should().Be("lead_closed");

            db.ProviderInfoRequests.Should().BeEmpty("a closed request must not re-open ops work");
            db.ProviderOutreaches.Single().Status.Should().Be(ProviderOutreachStatus.Sent);
            ops.Emails.Should().BeEmpty();
        }
    }

    // ─── Input hardening: normalised, or refused — never stored as junk ───────

    [Fact]
    public async Task NeedInfo_UnknownBlankAndDuplicateReasons_AreNormalisedAwayNotPersisted()
    {
        var db = TestDbContext.Create();
        var (_, _, _, token) = Seed(db);

        var dto = (await MakePublic(db, new CapturingEmailQueue()).NeedInfo(token, new NeedInfoRequest(
                Reasons: ["  PHOTOS ", "photos", "", "drop-tables", null!, "access"])))
            .Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<NeedInfoSubmittedDto>().Subject;

        dto.Reasons.Should().Equal(new[] { "photos", "access" },
            "case and padding are normalised, duplicates collapse, unknown slugs are dropped — " +
            "one stale checkbox must not cost us the whole reply");

        InfoRequestReasons.Parse(db.ProviderInfoRequests.Single().ReasonsJson)
            .Should().Equal("photos", "access");
    }

    [Fact]
    public async Task NeedInfo_WithNothingButUnknownReasonsAndNoNote_400_AndRecordsNothing()
    {
        var db = TestDbContext.Create();
        var (_, _, _, token) = Seed(db);
        var ops = new CapturingEmailQueue();
        var pub = MakePublic(db, ops);

        foreach (var payload in new[]
        {
            new NeedInfoRequest(),                                  // nothing at all
            new NeedInfoRequest([], "   "),                         // whitespace only
            new NeedInfoRequest(["drop-tables", "banana"], null),   // nothing recognisable
        })
        {
            (await pub.NeedInfo(token, payload)).Should().BeOfType<BadRequestObjectResult>(
                "a flag carrying no reason and no note would sit in the ops queue saying nothing");
        }

        db.ProviderInfoRequests.Should().BeEmpty();
        db.ProviderOutreaches.Single().Status.Should().Be(ProviderOutreachStatus.Sent,
            "a refused flag leaves the outreach exactly as it was");
        ops.Emails.Should().BeEmpty();
    }

    [Fact]
    public async Task NeedInfo_FreeTextOnly_IsAccepted_BecauseTheNoteIsThePoint()
    {
        var db = TestDbContext.Create();
        var (_, _, _, token) = Seed(db);

        var dto = (await MakePublic(db, new CapturingEmailQueue()).NeedInfo(
                token, new NeedInfoRequest(Note: "Kas klaver on esimesel korrusel?")))
            .Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<NeedInfoSubmittedDto>().Subject;

        dto.Reasons.Should().BeEmpty();
        dto.Note.Should().Be("Kas klaver on esimesel korrusel?");
        db.ProviderInfoRequests.Should().ContainSingle()
            .Which.Note.Should().Be("Kas klaver on esimesel korrusel?");
    }

    [Fact]
    public async Task NeedInfo_RejectsAngleBrackets_AndClampsALongNote()
    {
        var db = TestDbContext.Create();
        var (_, _, _, token) = Seed(db);
        var pub = MakePublic(db, new CapturingEmailQueue());

        (await pub.NeedInfo(token, new NeedInfoRequest([InfoRequestReasons.Other], "a <script> b")))
            .Should().BeOfType<BadRequestObjectResult>();
        db.ProviderInfoRequests.Should().BeEmpty();
        db.ProviderOutreaches.Single().Status.Should().Be(ProviderOutreachStatus.Sent);

        (await pub.NeedInfo(token, new NeedInfoRequest([InfoRequestReasons.Other], new string('x', 5000))))
            .Should().BeOfType<OkObjectResult>();
        db.ProviderInfoRequests.Single().Note!.Length.Should().Be(2000,
            "clamped to the column, never truncated by the database");
    }

    // ─── No customer PII, anywhere a provider can see ─────────────────────────

    [Fact]
    public async Task NeedInfo_NoResponseEverNamesTheCustomer()
    {
        var db = TestDbContext.Create();
        var (lead, _, _, token) = Seed(db);
        var pub = MakePublic(db, new CapturingEmailQueue());

        var bodies = new List<string>
        {
            BodyJson(await pub.NeedInfo("does-not-exist", new NeedInfoRequest([InfoRequestReasons.Photos]))),
            BodyJson(await pub.NeedInfo(token, new NeedInfoRequest())),
            BodyJson(await pub.NeedInfo(token, new NeedInfoRequest([InfoRequestReasons.Photos], "pictures please"))),
            BodyJson(await pub.GetQuote(token)),
        };

        lead.Status = DemandLeadStatus.Converted;
        await db.SaveChangesAsync();
        bodies.Add(BodyJson(await pub.NeedInfo(token, new NeedInfoRequest([InfoRequestReasons.Photos]))));

        foreach (var body in bodies)
        {
            body.Should().NotContain(CustomerEmail)
                .And.NotContain(CustomerName)
                .And.NotContain("5555 1234")
                .And.NotContain(token, "the credential is never echoed back into the payload");
        }
    }

    // ─── Read-back on the quote page ──────────────────────────────────────────

    [Fact]
    public async Task GetQuote_ReportsTheOpenAsk_AndGoesQuietOnceOpsResolvesIt()
    {
        var db = TestDbContext.Create();
        var (_, _, _, token) = Seed(db);
        var pub = MakePublic(db, new CapturingEmailQueue());

        var before = (await pub.GetQuote(token)).Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PublicQuoteDto>().Subject;
        before.InfoRequested.Should().BeFalse();
        before.InfoRequest.Should().BeNull();

        await pub.NeedInfo(token, new NeedInfoRequest([InfoRequestReasons.Photos], "need pictures"));

        var after = (await pub.GetQuote(token)).Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PublicQuoteDto>().Subject;
        after.InfoRequested.Should().BeTrue("re-opening the link must show we already have their ask");
        after.InfoRequest!.Reasons.Should().Equal("photos");
        after.InfoRequest.Note.Should().Be("need pictures");

        // Ops answers it — the page has nothing left to say and goes back to
        // simply asking for a price.
        db.ProviderInfoRequests.Single().ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var resolved = (await pub.GetQuote(token)).Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PublicQuoteDto>().Subject;
        resolved.InfoRequested.Should().BeFalse();
        resolved.InfoRequest.Should().BeNull();
    }

    // ─── Repeat presses, and facts that outrank a question ────────────────────

    [Fact]
    public async Task NeedInfo_Twice_UpdatesTheOneOpenAsk_WithoutErasingWhatWasSaidBefore()
    {
        var db = TestDbContext.Create();
        var (_, _, _, token) = Seed(db);
        var ops = new CapturingEmailQueue();
        var pub = MakePublic(db, ops);

        await pub.NeedInfo(token, new NeedInfoRequest(
            [InfoRequestReasons.Photos, InfoRequestReasons.Address], "and the floor?"));
        // Comes back having got the photos — a narrower tick set REPLACES, because
        // a checkbox set is submitted whole.
        await pub.NeedInfo(token, new NeedInfoRequest([InfoRequestReasons.Address]));

        var row = db.ProviderInfoRequests.Should().ContainSingle(
            "one provider blocked on one lead is one open question, not two").Subject;
        InfoRequestReasons.Parse(row.ReasonsJson).Should().Equal("address");
        row.Note.Should().Be("and the floor?", "an omitted note never destroys the one already sent");

        // But an EMPTY tick set is an omission, not a retraction.
        await pub.NeedInfo(token, new NeedInfoRequest(Note: "still waiting"));
        InfoRequestReasons.Parse(db.ProviderInfoRequests.Single().ReasonsJson).Should().Equal("address");
        db.ProviderInfoRequests.Single().Note.Should().Be("still waiting");

        ops.Emails.Should().HaveCount(3, "each press is a fresh nudge ops must see");
    }

    [Fact]
    public async Task NeedInfo_DoesNotOverwriteAStrongerOutreachFact()
    {
        foreach (var (start, expected) in new[]
        {
            // Silence — exactly what this feature exists to end.
            (ProviderOutreachStatus.Sent,       ProviderOutreachStatus.NeedsInfo),
            // An admin's guess that they never came back, disproved by this message.
            (ProviderOutreachStatus.NoAnswer,   ProviderOutreachStatus.NeedsInfo),
            // Already stronger facts: a price, a refusal, a dead address.
            (ProviderOutreachStatus.Replied,    ProviderOutreachStatus.Replied),
            (ProviderOutreachStatus.Declined,   ProviderOutreachStatus.Declined),
            (ProviderOutreachStatus.Bounced,    ProviderOutreachStatus.Bounced),
            (ProviderOutreachStatus.Complained, ProviderOutreachStatus.Complained),
        })
        {
            var db = TestDbContext.Create();
            var (_, _, outreach, token) = Seed(db);
            outreach.Status = start;
            await db.SaveChangesAsync();

            (await MakePublic(db, new CapturingEmailQueue())
                    .NeedInfo(token, new NeedInfoRequest([InfoRequestReasons.Items])))
                .Should().BeOfType<OkObjectResult>();

            db.ProviderOutreaches.Single().Status.Should().Be(expected, $"starting from {start}");
            db.ProviderInfoRequests.Should().ContainSingle(
                "the ask is recorded whatever the outreach already said");
        }
    }
}
