using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// The guards standing between a public form post and real businesses getting
/// cold-emailed: need-date sanity, duplicate submits, and attribution capture.
///
/// These matter more than ordinary input validation because
/// <c>POST /api/leads/request</c> is not a form that files a row — it fans out
/// immediately to up to six third-party providers. Every defect that gets past
/// here is spent as somebody's goodwill.
/// </summary>
public class ConciergeIntakeGuardTests
{
    private sealed class CapturingEmailQueue : IBackgroundEmailQueue
    {
        public List<(string To, string Subject, string TextBody, string? HtmlBody, string? ReplyTo)> Emails { get; } = [];
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null)
            => Emails.Add((to, subject, textBody, htmlBody, null));
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody, string? replyTo)
            => Emails.Add((to, subject, textBody, htmlBody, replyTo));
        public void EnqueueVerificationEmail(Guid userId) { }
    }

    private sealed class NoOpNotifications : INotificationService
    {
        public Task<PaginatedResult<NotificationDto>> GetAllAsync(Guid userId, int page = 1, int limit = 50)
            => Task.FromResult(new PaginatedResult<NotificationDto>([], 0, page, limit, false));
        public Task MarkReadAsync(Guid id, Guid userId) => Task.CompletedTask;
        public Task MarkAllReadAsync(Guid userId) => Task.CompletedTask;
        public Task CreateAsync(Guid userId, NotificationType type, string title, string desc,
            string? actionUrl = null, string? entityId = null, string? entityType = null)
            => Task.CompletedTask;
    }

    /// <summary>Counts how many times the intake actually fanned out.</summary>
    private sealed class CountingOutreach : IConciergeOutreachService
    {
        public int FanOutCalls { get; private set; }

        public Task<AutoOutreachSummary> AutoFanOutAsync(DemandLead lead, CancellationToken ct = default)
        {
            FanOutCalls++;
            return Task.FromResult(AutoOutreachSummary.Skipped("disabled"));
        }

        public Task<OutreachSendResult> SendAsync(
            DemandLead lead, IReadOnlyList<Guid> supplierIds, bool resend, string actor,
            CancellationToken ct = default) => Task.FromResult(OutreachSendResult.Empty);
    }

    private static SupportController MakeSupport(
        RuumlyDbContext db, IBackgroundEmailQueue queue, IConciergeOutreachService? outreach = null) =>
        new(db, queue, new NoOpNotifications(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            outreach ?? TestServices.Outreach(db, queue),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SupportController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static ConciergeRequest Request(
        DateTime? needDate = null, string? details = null, string city = "Tallinn",
        string email = "cust@x.ee", string? attribution = null,
        string? website = null, long? elapsedMs = null) =>
        new(Email: email, City: city, Name: "Cust", Phone: "+372 5000 0000",
            Categories: ["moving"], ToCity: null, NeedDate: needDate,
            Details: details, Language: "et", Attribution: attribution,
            Website: website, ElapsedMs: elapsedMs);

    // ─── Need-date sanity ─────────────────────────────────────────────────────

    [Fact]
    public async Task RequestConcierge_PastNeedDate_IsRejected_AndNobodyIsContacted()
    {
        var db       = TestDbContext.Create();
        var outreach = new CountingOutreach();

        // A mistyped year is the realistic case. It is not harmless: anything
        // within three days — past included — is flagged URGENT in the provider
        // subject line, so this would send a red-flagged cold email about a job
        // that already happened.
        var result = await MakeSupport(db, new CapturingEmailQueue(), outreach)
            .RequestConcierge(Request(needDate: DateTime.UtcNow.Date.AddDays(-1)));

        result.Should().BeOfType<BadRequestObjectResult>();
        db.DemandLeads.Should().BeEmpty("a rejected request must not leave a lead behind");
        outreach.FanOutCalls.Should().Be(0, "no provider may be contacted about a request we refused");
    }

    [Fact]
    public async Task RequestConcierge_AbsurdlyFarNeedDate_IsRejected()
    {
        var db = TestDbContext.Create();

        var result = await MakeSupport(db, new CapturingEmailQueue())
            .RequestConcierge(Request(needDate: DateTime.UtcNow.Date.AddYears(5)));

        result.Should().BeOfType<BadRequestObjectResult>();
        db.DemandLeads.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestConcierge_TodayIsAcceptable()
    {
        var db = TestDbContext.Create();

        // Same-day demand is real and common in this funnel ("I need a van today").
        var result = await MakeSupport(db, new CapturingEmailQueue())
            .RequestConcierge(Request(needDate: DateTime.UtcNow.Date));

        result.Should().BeOfType<OkObjectResult>();
        db.DemandLeads.Should().ContainSingle();
    }

    [Fact]
    public async Task RequestConcierge_NoNeedDate_StillAccepted()
    {
        var db = TestDbContext.Create();

        // The date is required in the UI for date-driven services, but the API
        // must stay tolerant: a lead with no date is worth far more than a 400.
        var result = await MakeSupport(db, new CapturingEmailQueue()).RequestConcierge(Request());

        result.Should().BeOfType<OkObjectResult>();
        db.DemandLeads.Single().NeedDate.Should().BeNull();
    }

    // ─── Duplicate submits ────────────────────────────────────────────────────

    [Fact]
    public async Task RequestConcierge_IdenticalRepeat_CreatesOneLead_AndFansOutOnce()
    {
        var db       = TestDbContext.Create();
        var queue    = new CapturingEmailQueue();
        var outreach = new CountingOutreach();
        var support  = MakeSupport(db, queue, outreach);

        var payload = Request(needDate: DateTime.UtcNow.Date.AddDays(10), details: "2-room flat");

        (await support.RequestConcierge(payload)).Should().BeOfType<OkObjectResult>();
        (await support.RequestConcierge(payload)).Should().BeOfType<OkObjectResult>(
            "the customer must never be told their request failed when it did not");

        db.DemandLeads.Should().ContainSingle("a double-tap is one request, not two");
        outreach.FanOutCalls.Should().Be(1,
            "a second fan-out would cold-email the same providers twice about one customer, " +
            "each copy carrying its own quote token");
    }

    [Fact]
    public async Task RequestConcierge_CorrectedResubmit_IsANewRequest()
    {
        var db       = TestDbContext.Create();
        var outreach = new CountingOutreach();
        var support  = MakeSupport(db, new CapturingEmailQueue(), outreach);

        await support.RequestConcierge(Request(city: "Tallinn"));
        // Same person, corrected city — a real second request, not a repeat.
        await support.RequestConcierge(Request(city: "Tartu"));

        db.DemandLeads.Should().HaveCount(2);
        outreach.FanOutCalls.Should().Be(2);
    }

    [Fact]
    public async Task RequestConcierge_SamePayloadFromADifferentPerson_IsNotADuplicate()
    {
        var db      = TestDbContext.Create();
        var support = MakeSupport(db, new CapturingEmailQueue());

        await support.RequestConcierge(Request(email: "a@x.ee"));
        await support.RequestConcierge(Request(email: "b@x.ee"));

        db.DemandLeads.Should().HaveCount(2, "two neighbours moving the same week are two customers");
    }

    [Fact]
    public async Task RequestConcierge_RoutedLeadsAreNeverTreatedAsDuplicates()
    {
        var db = TestDbContext.Create();
        db.DemandLeads.Add(new DemandLead
        {
            Id = Guid.NewGuid(), Email = "cust@x.ee", City = "Tallinn",
            Category = DemandLeadCategory.Moving, Source = "routed",
            CreatedAt = DateTime.UtcNow,
        });
        db.SaveChanges();

        await MakeSupport(db, new CapturingEmailQueue()).RequestConcierge(Request());

        db.DemandLeads.Should().HaveCount(2,
            "a listing-routed quote request and a concierge request are different asks");
    }

    // ─── Attribution ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RequestConcierge_StoresAttribution()
    {
        var db = TestDbContext.Create();

        await MakeSupport(db, new CapturingEmailQueue()).RequestConcierge(
            Request(attribution: "utm_source=google|utm_medium=cpc|utm_campaign=kolimine|lp=/et/request"));

        db.DemandLeads.Single().Attribution
            .Should().Be("utm_source=google|utm_medium=cpc|utm_campaign=kolimine|lp=/et/request");
    }

    [Fact]
    public async Task RequestConcierge_ClampsOverlongAttribution_RatherThanFailing()
    {
        var db = TestDbContext.Create();

        await MakeSupport(db, new CapturingEmailQueue())
            .RequestConcierge(Request(attribution: new string('x', 5_000)));

        // A hostile or merely enthusiastic query string must never cost us a lead.
        db.DemandLeads.Single().Attribution!.Length.Should().Be(300);
    }

    [Fact]
    public async Task RequestConcierge_WithoutAttribution_LeavesItNull()
    {
        var db = TestDbContext.Create();

        await MakeSupport(db, new CapturingEmailQueue()).RequestConcierge(Request());

        db.DemandLeads.Single().Attribution.Should().BeNull("direct traffic is a real answer");
    }

    // ─── Automation signals ───────────────────────────────────────────────────
    //
    // The contract these all share: a suspected bot's lead is STILL SAVED and
    // only the automatic fan-out is withheld. Silently dropping a submission
    // would show a real customer a success screen while Ruumly got nothing —
    // strictly worse than the spam being guarded against.

    [Fact]
    public async Task FilledHoneypot_SavesTheLeadButContactsNobody()
    {
        var db       = TestDbContext.Create();
        var outreach = new CountingOutreach();

        var result = await MakeSupport(db, new CapturingEmailQueue(), outreach)
            .RequestConcierge(Request(website: "http://spam.example"));

        result.Should().BeOfType<OkObjectResult>();
        db.DemandLeads.Should().ContainSingle("the lead is evidence, and may yet be real");
        outreach.FanOutCalls.Should().Be(0,
            "one submit emails up to six real businesses — that is what must be withheld");
    }

    [Fact]
    public async Task HeldLead_TellsTheOperatorWhy()
    {
        var db = TestDbContext.Create();

        await MakeSupport(db, new CapturingEmailQueue(), new CountingOutreach())
            .RequestConcierge(Request(website: "x"));

        // Written where the operator actually looks, so a held lead is reviewable
        // rather than mysteriously uncontacted.
        db.DemandLeads.Single().AdminNotes.Should().NotBeNullOrWhiteSpace();
        db.DemandLeads.Single().AdminNotes.Should().Contain("hidden field");
        db.DemandLeads.Single().Status.Should().Be(DemandLeadStatus.New,
            "a held lead sits in the normal queue, it is not a separate state");
    }

    [Fact]
    public async Task HeldLead_IsReportedInTheOpsAlert()
    {
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();

        await MakeSupport(db, queue, new CountingOutreach())
            .RequestConcierge(Request(website: "x"));

        var alert = queue.Emails.Single(e => e.To == "info@ruumly.eu");
        alert.TextBody.Should().Contain("HELD");
    }

    [Fact]
    public async Task ImplausiblyFastSubmit_IsHeld()
    {
        var db       = TestDbContext.Create();
        var outreach = new CountingOutreach();

        await MakeSupport(db, new CapturingEmailQueue(), outreach)
            .RequestConcierge(Request(elapsedMs: 900));

        db.DemandLeads.Should().ContainSingle();
        outreach.FanOutCalls.Should().Be(0);
    }

    [Fact]
    public async Task AHumanPacedSubmit_FansOutNormally()
    {
        var db       = TestDbContext.Create();
        var outreach = new CountingOutreach();

        await MakeSupport(db, new CapturingEmailQueue(), outreach)
            .RequestConcierge(Request(elapsedMs: 45_000, website: ""));

        outreach.FanOutCalls.Should().Be(1);
        db.DemandLeads.Single().AdminNotes.Should().BeNull();
    }

    [Fact]
    public async Task AbsentTiming_IsTreatedAsUnknown_NotAsSuspicious()
    {
        var db       = TestDbContext.Create();
        var outreach = new CountingOutreach();

        // The SPA is served through a service worker, so an older cached bundle
        // does not send this field. Treating its silence as bot-like would
        // quietly stop fanning out real requests for every customer still on it.
        await MakeSupport(db, new CapturingEmailQueue(), outreach)
            .RequestConcierge(Request(elapsedMs: null));

        outreach.FanOutCalls.Should().Be(1);
    }

    [Fact]
    public async Task ManyRequestsFromOneAddressInADay_StopFanningOut()
    {
        var db       = TestDbContext.Create();
        var outreach = new CountingOutreach();
        var support  = MakeSupport(db, new CapturingEmailQueue(), outreach);

        // Distinct payloads so the duplicate guard does not absorb them — this
        // is the volume signal, which is the one a hand-rolled POST cannot omit.
        for (var i = 0; i < 7; i++)
            await support.RequestConcierge(Request(city: $"City{i}"));

        db.DemandLeads.Should().HaveCount(7, "every submission is still recorded");
        outreach.FanOutCalls.Should().Be(5,
            "provider goodwill is the bounded resource — the cap is per address per day");
    }

    [Fact]
    public async Task TheCapIsPerAddress_NotGlobal()
    {
        var db       = TestDbContext.Create();
        var outreach = new CountingOutreach();
        var support  = MakeSupport(db, new CapturingEmailQueue(), outreach);

        for (var i = 0; i < 6; i++)
            await support.RequestConcierge(Request(email: $"person{i}@x.ee"));

        outreach.FanOutCalls.Should().Be(6, "six different customers are six real requests");
    }

    // ─── Contact form ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Contact_InvalidEmail_IsRejected()
    {
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();

        var result = await MakeSupport(db, queue).Contact(
            new ContactRequest(Name: "Cust", Email: "not-an-email", Subject: "Hi", Message: "Hello"));

        result.Should().BeOfType<BadRequestObjectResult>();
        queue.Emails.Should().BeEmpty(
            "the address is the only way back to this person — an unreplyable note helps nobody");
    }
}
