using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Ruumly.Backend.Constants;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ruumly.Backend.Tests;

/// <summary>
/// The OTHER half of "a provider cannot quote this yet": ops answering it.
///
/// Raising the flag shipped first (see <see cref="ProviderInfoRequestTests"/>)
/// and nothing could lower it — ResolvedAt was write-only in the schema and
/// never written. A flag with no off switch stops meaning "blocked now" within a
/// week and starts meaning "was blocked once", which is worse than no flag: the
/// quote page kept telling a provider we owed them an answer after we had sent
/// it, and the outreach row never rejoined the queue.
///
/// What these pin: the ask closes, the outreach comes back to a state that is
/// TRUE (Sent — not Replied, which is counted as a supplier match), a resolved
/// ask disappears from both surfaces that read it, pressing twice is harmless,
/// and nobody but an admin can press it at all.
/// </summary>
public class AdminInfoRequestResolveTests
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

    private static ClaimsPrincipal Principal(string role) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim(ClaimTypes.Email, $"{role.ToLowerInvariant()}@ruumly.eu"),
        ], "test"));

    private static ControllerContext AdminContext() =>
        new() { HttpContext = new DefaultHttpContext { User = Principal("Admin") } };

    private static AdminLeadsController MakeLeads(RuumlyDbContext db) =>
        new(db, TestServices.NoEmail()) { ControllerContext = AdminContext() };

    private static AdminOffersController MakeOffers(RuumlyDbContext db, IBackgroundEmailQueue queue) =>
        new(db, queue, TestServices.Config(), TestServices.Outreach(db, queue, TestServices.Config()),
            TestServices.OutcomeNotifier(db, queue),
            NullLogger<AdminOffersController>.Instance)
        {
            ControllerContext = AdminContext(),
        };

    private static QuoteController MakeQuote(RuumlyDbContext db, IBackgroundEmailQueue queue) =>
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

    private static object? Prop(object o, string name) =>
        o.GetType().GetProperty(name)!.GetValue(o);

    /// <summary>
    /// A worked concierge lead with one contacted provider holding a quote token.
    /// Status is Contacted (not New) deliberately: the metrics test needs a lead
    /// that has LEFT New, because an untouched request is not yet in the supplier
    /// match-rate base at all.
    /// </summary>
    private static (DemandLead Lead, Supplier Supplier, ProviderOutreach Outreach, string Token)
        Seed(RuumlyDbContext db)
    {
        var now = DateTime.UtcNow;
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "cust@x.ee", Name = "Mari Maasikas",
            City = "Tallinn", ToCity = "Haapsalu", Category = DemandLeadCategory.Moving,
            Details = "1-room flat, one piano", Language = "et", Source = "concierge",
            Status = DemandLeadStatus.Contacted, ContactedAt = now.AddMinutes(-30),
            CreatedAt = now.AddDays(-1),
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
            SentTo = supplier.ContactEmail, SentAt = now.AddHours(-4),
            Status = ProviderOutreachStatus.Sent, QuoteToken = token,
        };

        db.DemandLeads.Add(lead);
        db.Suppliers.Add(supplier);
        db.ProviderOutreaches.Add(outreach);
        db.SaveChanges();
        return (lead, supplier, outreach, token);
    }

    /// <summary>Raises a real ask through the provider-facing endpoint, as a provider would.</summary>
    private static async Task<ProviderInfoRequest> Block(
        RuumlyDbContext db, string token,
        List<string>? reasons = null, string? note = "Kas peale- ja mahalaadimine on samal aadressil?")
    {
        (await MakeQuote(db, new CapturingEmailQueue()).NeedInfo(token, new NeedInfoRequest(
                Reasons: reasons ?? [InfoRequestReasons.Address, InfoRequestReasons.Photos],
                Note: note)))
            .Should().BeOfType<OkObjectResult>();
        return db.ProviderInfoRequests.Single();
    }

    /// <summary>The one outreach row as the admin workspace actually receives it.</summary>
    private static async Task<object> WorkspaceRow(RuumlyDbContext db, Guid leadId)
    {
        var rows = (await MakeOffers(db, new CapturingEmailQueue()).GetLeadOutreach(leadId))
            .Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<System.Collections.IEnumerable>().Subject
            .Cast<object>().ToList();
        return rows.Should().ContainSingle().Subject;
    }

    // ─── The happy path ───────────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_StampsResolvedAt_AndReturnsTheOutreachToSent()
    {
        var db = TestDbContext.Create();
        var (_, _, outreach, token) = Seed(db);
        var ask = await Block(db, token);

        db.ProviderOutreaches.Single().Status.Should().Be(ProviderOutreachStatus.NeedsInfo,
            "precondition: raising the ask is what blocks the row");

        var body = (await MakeLeads(db).ResolveInfoRequest(ask.Id))
            .Should().BeOfType<OkObjectResult>().Subject.Value!;

        db.ProviderInfoRequests.Single().ResolvedAt.Should().NotBeNull(
            "the whole point: the flag can now be lowered");
        Prop(body, "resolvedAt").Should().NotBeNull();
        Prop(body, "providerOutreachId").Should().Be(outreach.Id);

        db.ProviderOutreaches.Single().Status.Should().Be(ProviderOutreachStatus.Sent,
            "answered means 'contacted, still no price' — which is exactly Sent");
        Prop(body, "outreachStatus").Should().Be("sent");
    }

    [Fact]
    public async Task Resolve_NeverMarksTheOutreachReplied_BecauseThatWouldBeAMatch()
    {
        var db = TestDbContext.Create();
        var (_, _, _, token) = Seed(db);
        var ask = await Block(db, token);

        await MakeLeads(db).ResolveInfoRequest(ask.Id);

        db.ProviderOutreaches.Single().Status.Should().NotBe(ProviderOutreachStatus.Replied,
            "GetLeadMetrics counts a Replied outreach as a supplier MATCH — answering a " +
            "provider's question is us unblocking them, not them agreeing to serve the job");
        db.ProviderOutreaches.Single().Status.Should().NotBe(ProviderOutreachStatus.NoAnswer,
            "NoAnswer is a guess that they never came back, and their question disproves it");
    }

    [Fact]
    public async Task Resolve_WritesAnAuditTrailNamingWhatTheProviderWasBlockedOn()
    {
        var db = TestDbContext.Create();
        var (lead, supplier, _, token) = Seed(db);
        var ask = await Block(db, token);

        await MakeLeads(db).ResolveInfoRequest(ask.Id);

        var entry = db.AuditLogs.Should().ContainSingle().Subject;
        entry.Action.Should().Be("lead.info_request_resolved");
        entry.Target.Should().Be(ask.Id.ToString());
        entry.Detail.Should().Contain(lead.Id.ToString())
            .And.Contain(supplier.Id.ToString())
            .And.Contain("address", "once resolved the row leaves the workspace — " +
                "the audit trail is the only surviving record of what was asked");
    }

    // ─── Idempotence ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_Twice_IsIdempotent_AndDoesNotRewriteWhenWeAnswered()
    {
        var db = TestDbContext.Create();
        var (_, _, _, token) = Seed(db);
        var ask = await Block(db, token);
        var admin = MakeLeads(db);

        (await admin.ResolveInfoRequest(ask.Id)).Should().BeOfType<OkObjectResult>();
        var firstResolvedAt = db.ProviderInfoRequests.Single().ResolvedAt;

        (await admin.ResolveInfoRequest(ask.Id)).Should().BeOfType<OkObjectResult>(
            "a double-click mid-loop must not be an error the operator has to interpret");

        db.ProviderInfoRequests.Single().ResolvedAt.Should().Be(firstResolvedAt,
            "re-stamping would rewrite when we actually answered them");
        db.AuditLogs.Should().ContainSingle("nothing changed, so there is nothing to audit");
    }

    [Fact]
    public async Task Resolve_Twice_DoesNotDragAMovedOnOutreachBackToSent()
    {
        var db = TestDbContext.Create();
        var (_, _, _, token) = Seed(db);
        var ask = await Block(db, token);
        var admin = MakeLeads(db);

        await admin.ResolveInfoRequest(ask.Id);

        // The provider answers the question with an actual price.
        db.ProviderOutreaches.Single().Status = ProviderOutreachStatus.Replied;
        await db.SaveChangesAsync();

        await admin.ResolveInfoRequest(ask.Id);

        db.ProviderOutreaches.Single().Status.Should().Be(ProviderOutreachStatus.Replied,
            "a second press must not erase the quote that arrived in the meantime");
    }

    [Fact]
    public async Task Resolve_LeavesAStrongerOutreachFactAlone()
    {
        foreach (var stronger in new[]
        {
            // A submitted price, a refusal, and two dead addresses. Each is a
            // harder fact than "we answered their question".
            ProviderOutreachStatus.Replied,
            ProviderOutreachStatus.Declined,
            ProviderOutreachStatus.Bounced,
            ProviderOutreachStatus.Complained,
        })
        {
            var db = TestDbContext.Create();
            var (_, _, _, token) = Seed(db);
            var ask = await Block(db, token);

            db.ProviderOutreaches.Single().Status = stronger;
            await db.SaveChangesAsync();

            (await MakeLeads(db).ResolveInfoRequest(ask.Id)).Should().BeOfType<OkObjectResult>();

            db.ProviderInfoRequests.Single().ResolvedAt.Should().NotBeNull(
                $"the ask still closes when the row is {stronger}");
            db.ProviderOutreaches.Single().Status.Should().Be(stronger,
                $"{stronger} outranks 'we answered them' and must survive");
        }
    }

    // ─── The resolved ask disappears from both surfaces that read it ──────────

    [Fact]
    public async Task Resolve_StopsTheAskAppearingAsOpen_InTheWorkspaceAndOnTheQuotePage()
    {
        var db = TestDbContext.Create();
        var (lead, _, _, token) = Seed(db);
        var ask = await Block(db, token);

        // Before: the workspace shows WHY the provider is blocked, not just that.
        var blocked = Prop(await WorkspaceRow(db, lead.Id), "infoRequest");
        blocked.Should().NotBeNull("an outreach on needsinfo without its question is unactionable");
        Prop(blocked!, "id").Should().Be(ask.Id);
        ((IEnumerable<string>)Prop(blocked!, "reasons")!).Should().BeEquivalentTo(["address", "photos"]);
        Prop(blocked!, "note").Should().Be("Kas peale- ja mahalaadimine on samal aadressil?");

        var beforeQuote = (await MakeQuote(db, new CapturingEmailQueue()).GetQuote(token))
            .Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PublicQuoteDto>().Subject;
        beforeQuote.InfoRequested.Should().BeTrue();

        await MakeLeads(db).ResolveInfoRequest(ask.Id);

        // After: gone from the ops list…
        Prop(await WorkspaceRow(db, lead.Id), "infoRequest").Should().BeNull(
            "a closed question must leave the workspace, or the operator answers it again");

        // …and the provider's own page goes back to simply asking for a price.
        var afterQuote = (await MakeQuote(db, new CapturingEmailQueue()).GetQuote(token))
            .Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PublicQuoteDto>().Subject;
        afterQuote.InfoRequested.Should().BeFalse(
            "telling a provider we still owe them an answer after sending it is the bug");
        afterQuote.InfoRequest.Should().BeNull();
    }

    // ─── The north-star metric must not move ──────────────────────────────────

    [Fact]
    public async Task Resolve_DoesNotMakeTheLeadCountAsMatched_InTheNorthStarMetrics()
    {
        var db = TestDbContext.Create();
        var (_, _, _, token) = Seed(db);
        var ask = await Block(db, token);
        var admin = MakeLeads(db);

        static (int Matched, int Total) MatchRate(object metrics)
        {
            var rate = Prop(metrics, "matchRate30d")!;
            return ((int)Prop(rate, "matched")!, (int)Prop(rate, "total")!);
        }

        var before = MatchRate((await admin.GetLeadMetrics())
            .Should().BeOfType<OkObjectResult>().Subject.Value!);
        before.Should().Be((0, 1), "a worked-but-blocked request is in the base and is not a match");

        await admin.ResolveInfoRequest(ask.Id);

        MatchRate((await admin.GetLeadMetrics())
                .Should().BeOfType<OkObjectResult>().Subject.Value!)
            .Should().Be((0, 1),
                "answering a provider's question is not that provider agreeing to serve the job — " +
                "counting it would inflate supplier match rate with the requests we are stuck on");

        // The metric really does read this field, so the assertion above is not
        // vacuously true: an actual reply moves it.
        db.ProviderOutreaches.Single().Status = ProviderOutreachStatus.Replied;
        await db.SaveChangesAsync();
        MatchRate((await admin.GetLeadMetrics())
                .Should().BeOfType<OkObjectResult>().Subject.Value!)
            .Should().Be((1, 1), "a genuine reply IS a match — which is why resolve must not fake one");
    }

    // ─── Guards ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_UnknownId_Returns404_AndTouchesNothing()
    {
        var db = TestDbContext.Create();
        var (_, _, _, token) = Seed(db);
        await Block(db, token);

        (await MakeLeads(db).ResolveInfoRequest(Guid.NewGuid()))
            .Should().BeOfType<NotFoundObjectResult>();

        db.ProviderInfoRequests.Single().ResolvedAt.Should().BeNull("the real ask is untouched");
        db.ProviderOutreaches.Single().Status.Should().Be(ProviderOutreachStatus.NeedsInfo);
        db.AuditLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task Resolve_UnauthenticatedOrNonAdminCaller_IsRefused()
    {
        var method = typeof(AdminLeadsController)
            .GetMethod(nameof(AdminLeadsController.ResolveInfoRequest))!;
        var authData = method.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(typeof(AdminLeadsController).GetCustomAttributes<AuthorizeAttribute>(inherit: true))
            .ToList();

        authData.Should().NotBeEmpty(
            "closing a provider's question is an ops decision — it must never be anonymous");

        // Evaluate the real policy the attributes produce rather than trusting the string.
        await using var services = new ServiceCollection()
            .AddLogging().AddAuthorization().BuildServiceProvider();
        var policy = (await AuthorizationPolicy.CombineAsync(
            services.GetRequiredService<IAuthorizationPolicyProvider>(), authData))!;
        var authorization = services.GetRequiredService<IAuthorizationService>();

        (await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), null, policy))
            .Succeeded.Should().BeFalse("an anonymous caller must be refused");
        (await authorization.AuthorizeAsync(Principal("Provider"), null, policy))
            .Succeeded.Should().BeFalse("a provider must not be able to close their own question");
        (await authorization.AuthorizeAsync(Principal("Customer"), null, policy))
            .Succeeded.Should().BeFalse();
        (await authorization.AuthorizeAsync(Principal("Admin"), null, policy))
            .Succeeded.Should().BeTrue("ops runs this loop by hand");
    }

    // ─── The read side, on its own ────────────────────────────────────────────

    [Fact]
    public async Task Workspace_ShowsAFreeTextOnlyAsk_WithNoReasonsTicked()
    {
        var db = TestDbContext.Create();
        var (lead, _, _, token) = Seed(db);
        await Block(db, token, reasons: [], note: "Kas klaver on esimesel korrusel?");

        var blocked = Prop(await WorkspaceRow(db, lead.Id), "infoRequest")!;
        ((IEnumerable<string>)Prop(blocked, "reasons")!).Should().BeEmpty();
        Prop(blocked, "note").Should().Be("Kas klaver on esimesel korrusel?",
            "the checkboxes say which question; the note says what the question is");
    }

    [Fact]
    public async Task Workspace_ReportsNoAskOnAnOutreachNobodyIsBlockedOn()
    {
        var db = TestDbContext.Create();
        var (lead, _, _, _) = Seed(db);

        Prop(await WorkspaceRow(db, lead.Id), "infoRequest").Should().BeNull();
    }
}
