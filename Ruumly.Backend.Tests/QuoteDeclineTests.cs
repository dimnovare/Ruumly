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
/// The third answer on the tokenized quote page: the provider says NO, and the
/// no is RECORDED. Until 2026-08-20 the outreach email invited "a short 'not
/// possible' is a perfectly good answer" while the only way to give it was a
/// free-text reply into a shared inbox — so every real decline counted as
/// silence in the provider-silence metric, and the same provider kept being
/// fanned out to.
///
/// The properties that matter: a bare no is a complete answer, the outreach
/// stops reading as silence, a submitted price cannot be silently retracted, a
/// closed lead takes no decline, and re-opening the link shows the recorded
/// decline rather than the price form.
/// </summary>
public class QuoteDeclineTests
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

    private static QuoteController Make(RuumlyDbContext db, IBackgroundEmailQueue queue) =>
        new(db, queue,
            new Ruumly.Backend.Services.Implementations.OfferAutoSendService(
                db, queue,
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                TestServices.OutcomeNotifier(db, queue),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<
                    Ruumly.Backend.Services.Implementations.OfferAutoSendService>.Instance),
            new TestServices.NoStorage(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<QuoteController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static (DemandLead Lead, Supplier Supplier, ProviderOutreach Outreach, string Token)
        Seed(RuumlyDbContext db)
    {
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "cust@x.ee", Name = "Mari Maasikas",
            City = "Tallinn", ToCity = "Tartu", Category = DemandLeadCategory.Moving,
            Details = "2-room flat", Language = "et", Source = "concierge",
            Status = DemandLeadStatus.Contacted, ContactedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = "Kolimisfirma OÜ", ContactName = "C",
            ContactEmail = "info@kolija.ee", ContactPhone = "1", IsActive = true, Country = "EE",
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

    // ─── A bare no is a complete answer ───────────────────────────────────────

    [Fact]
    public async Task Decline_WithEmptyBody_RecordsDecline_AndAlertsOpsWithTheLeadReference()
    {
        var db = TestDbContext.Create();
        var (lead, _, _, token) = Seed(db);
        var ops = new CapturingEmailQueue();

        var result = await Make(db, ops).Decline(token, new DeclineQuoteRequest());

        var dto = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<QuoteDeclinedDto>().Subject;
        dto.Ok.Should().BeTrue();
        dto.Reason.Should().BeNull("a bare no needs no itemisation");

        var row = db.ProviderOutreaches.Single();
        row.Status.Should().Be(ProviderOutreachStatus.Declined,
            "the whole point: a real no must stop reading as silence");
        row.DeclinedAt.Should().NotBeNull();
        row.DeclineReason.Should().BeNull();

        var mail = ops.Emails.Should().ContainSingle().Subject;
        mail.Subject.Should().Contain(ProviderOutreachComposer.Reference(lead.Id),
            "two live Tallinn→Tartu moves produce identical subjects without the handle");
        mail.TextBody.Should().NotContain("cust@x.ee").And.NotContain("Mari",
            "no response or alert about a decline needs the customer's identity");
    }

    [Fact]
    public async Task Decline_WithReasonAndNote_RecordsBoth_AndUnknownReasonCollapsesToNull()
    {
        var db = TestDbContext.Create();
        var (_, _, _, token) = Seed(db);

        var result = await Make(db, new CapturingEmailQueue()).Decline(
            token, new DeclineQuoteRequest(DeclineReasons.WrongArea, "Me ei sõida Tartusse."));
        var dto = ((OkObjectResult)result).Value.Should().BeOfType<QuoteDeclinedDto>().Subject;
        dto.Reason.Should().Be("wrong_area");
        db.ProviderOutreaches.Single().DeclineNote.Should().Be("Me ei sõida Tartusse.");

        // A slug this build does not know degrades to a bare decline, never a 400 —
        // a stale cached page must not cost us the answer.
        var db2 = TestDbContext.Create();
        var (_, _, _, token2) = Seed(db2);
        var r2 = await Make(db2, new CapturingEmailQueue()).Decline(
            token2, new DeclineQuoteRequest("something_new"));
        ((QuoteDeclinedDto)((OkObjectResult)r2).Value!).Reason.Should().BeNull();
        db2.ProviderOutreaches.Single().Status.Should().Be(ProviderOutreachStatus.Declined);
    }

    // ─── What a decline must NOT do ───────────────────────────────────────────

    [Fact]
    public async Task Decline_AfterASubmittedPrice_Is409_AndThePriceSurvives()
    {
        var db = TestDbContext.Create();
        var (_, _, outreach, token) = Seed(db);
        outreach.Status       = ProviderOutreachStatus.Replied;
        outreach.QuotedAmount = 350m;
        outreach.QuotedAt     = DateTime.UtcNow;
        db.SaveChanges();

        var result = await Make(db, new CapturingEmailQueue()).Decline(token, new DeclineQuoteRequest());

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        System.Text.Json.JsonSerializer.Serialize(conflict.Value).Should().Contain("already_quoted");
        var row = db.ProviderOutreaches.Single();
        row.Status.Should().Be(ProviderOutreachStatus.Replied,
            "that price is live on a draft offer — withdrawing it is a conversation, not a button");
        row.QuotedAmount.Should().Be(350m);
    }

    [Fact]
    public async Task Decline_OnAClosedLead_Is409_WithTheMachineReadableReason()
    {
        foreach (var terminal in new[]
                 { DemandLeadStatus.Converted, DemandLeadStatus.Dismissed, DemandLeadStatus.Unmatched })
        {
            var db = TestDbContext.Create();
            var (lead, _, _, token) = Seed(db);
            lead.Status = terminal;
            db.SaveChanges();

            var result = await Make(db, new CapturingEmailQueue()).Decline(token, new DeclineQuoteRequest());
            var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
            System.Text.Json.JsonSerializer.Serialize(conflict.Value).Should().Contain("lead_closed");
            db.ProviderOutreaches.Single().Status.Should().Be(ProviderOutreachStatus.Sent,
                $"a {terminal} lead takes no decline");
        }
    }

    [Fact]
    public async Task Decline_UnknownToken_IsIndistinguishableFromMissing()
    {
        var db = TestDbContext.Create();
        Seed(db);
        var ops = new CapturingEmailQueue();

        var result = await Make(db, ops).Decline("no-such-token", new DeclineQuoteRequest());

        result.Should().BeOfType<NotFoundObjectResult>();
        ops.Emails.Should().BeEmpty();
    }

    // ─── Idempotence and the page's read-back ─────────────────────────────────

    [Fact]
    public async Task Decline_Twice_UpdatesReason_KeepsFirstTimestamp_AndKeepsNoteOnBlankRepeat()
    {
        var db = TestDbContext.Create();
        var (_, _, _, token) = Seed(db);
        var ctrl = Make(db, new CapturingEmailQueue());

        await ctrl.Decline(token, new DeclineQuoteRequest(DeclineReasons.NoCapacity, "Sel nädalal ei saa."));
        var firstAt = db.ProviderOutreaches.Single().DeclinedAt;

        await ctrl.Decline(token, new DeclineQuoteRequest(DeclineReasons.WrongArea));

        var row = db.ProviderOutreaches.Single();
        row.DeclineReason.Should().Be("wrong_area", "changing the reason is being MORE helpful");
        row.DeclineNote.Should().Be("Sel nädalal ei saa.",
            "a blank note on a repeat press is an omission, not a retraction");
        row.DeclinedAt.Should().Be(firstAt);
    }

    [Fact]
    public async Task Get_AfterDecline_ReportsDeclined_SoThePageShowsTheAnswerInsteadOfTheForm()
    {
        var db = TestDbContext.Create();
        var (_, _, _, token) = Seed(db);
        var ctrl = Make(db, new CapturingEmailQueue());
        await ctrl.Decline(token, new DeclineQuoteRequest(DeclineReasons.TooSmall));

        var get = await ctrl.GetQuote(token);

        var dto = ((OkObjectResult)get).Value.Should().BeOfType<PublicQuoteDto>().Subject;
        dto.Declined.Should().BeTrue();
        dto.Closed.Should().BeFalse("the lead itself is still open — other providers may yet quote");
    }
}
