using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Bounces used to die inside Resend: the outreach row still said "sent" and the
/// dead address kept winning auto fan-out slots forever. These tests pin the
/// whole loop — webhook in, supplier + outreach row marked, fan-out skips the
/// dead provider and picks the next one, and an admin saving a phone-captured
/// address puts the provider back in the running.
/// </summary>
public class EmailBounceTests
{
    private const string Secret = "whsec_cnV1bWx5LXRlc3Qtd2ViaG9vay1zZWNyZXQ=";

    private sealed class CapturingEmailQueue : IBackgroundEmailQueue
    {
        public List<string> Recipients { get; } = [];
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null)
            => Recipients.Add(to);
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody, string? replyTo)
            => Recipients.Add(to);
        public void EnqueueVerificationEmail(Guid userId) { }
    }

    // ─── Webhook plumbing ─────────────────────────────────────────────────────

    // Built by concatenation, not interpolation: the raw JSON ends in a run of
    // closing braces that fights every raw-string interpolation depth.
    private static string BouncePayload(
        string recipient, string bounceType = "Permanent", string type = "email.bounced") =>
        "{\"type\":\"" + type + "\",\"created_at\":\"2026-08-10T09:15:00.000Z\",\"data\":{"
        + "\"email_id\":\"e1\",\"from\":\"Ruumly <info@ruumly.eu>\",\"to\":[\"" + recipient + "\"],"
        + "\"subject\":\"Klient otsib teenust\",\"bounce\":{\"type\":\"" + bounceType + "\","
        + "\"subType\":\"NoEmail\",\"message\":\"Recipient address does not exist\"}}}";

    private static string ComplaintPayload(string recipient) =>
        "{\"type\":\"email.complained\",\"created_at\":\"2026-08-10T09:15:00.000Z\",\"data\":{"
        + "\"email_id\":\"e2\",\"to\":[\"" + recipient + "\"],\"subject\":\"Klient otsib teenust\"}}";

    /// <summary>email.delivered / email.opened — the receipts, not the failures.</summary>
    private static string ReceiptPayload(string recipient, string type, DateTime? at = null) =>
        "{\"type\":\"" + type + "\",\"created_at\":\""
        + (at ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + "\",\"data\":{"
        + "\"email_id\":\"e3\",\"from\":\"Ruumly <info@ruumly.eu>\",\"to\":[\"" + recipient + "\"],"
        + "\"subject\":\"Klient otsib teenust\"}}";

    private static string DeliveredPayload(string recipient, DateTime? at = null) =>
        ReceiptPayload(recipient, "email.delivered", at);

    private static string OpenedPayload(string recipient, DateTime? at = null) =>
        ReceiptPayload(recipient, "email.opened", at);

    private static ResendWebhookController MakeWebhook(
        RuumlyDbContext db, string body, string eventId,
        string? secret = Secret, bool validSignature = true, long? timestampOverride = null)
    {
        var controller = new ResendWebhookController(
            new EmailDeliveryTracker(db, NullLogger<EmailDeliveryTracker>.Instance),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Resend:WebhookSecret"] = secret })
                .Build(),
            NullLogger<ResendWebhookController>.Instance);

        var timestamp = timestampOverride ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var bytes = Encoding.UTF8.GetBytes(body);
        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(bytes);
        http.Request.ContentLength = bytes.Length;
        http.Request.Headers["svix-id"] = eventId;
        http.Request.Headers["svix-timestamp"] = timestamp.ToString();
        http.Request.Headers["svix-signature"] = validSignature
            ? Sign(eventId, timestamp, body)
            : "v1,ZGVmaW5pdGVseS1ub3QtdGhlLXNpZ25hdHVyZQ==";

        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static string Sign(string id, long timestamp, string body)
    {
        var key = Convert.FromBase64String(Secret["whsec_".Length..]);
        return "v1," + Convert.ToBase64String(
            HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{id}.{timestamp}.{body}")));
    }

    private static async Task<(RuumlyDbContext Db, Supplier Supplier, DemandLead Lead, ProviderOutreach Row)>
        ContactedProviderAsync(string email = "dead@kolimine.ee")
    {
        var db = TestDbContext.Create();
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = "Kolimisabi OÜ", ContactName = "C",
            ContactEmail = email, ContactPhone = "+372 555 0001", IsActive = true,
        };
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "customer@example.ee", City = "Tallinn",
            Category = DemandLeadCategory.Moving, Language = "et", Source = "concierge",
            Status = DemandLeadStatus.Contacted,
        };
        var row = new ProviderOutreach
        {
            Id = Guid.NewGuid(), DemandLeadId = lead.Id, SupplierId = supplier.Id,
            SentTo = email, SentAt = DateTime.UtcNow.AddMinutes(-2),
            Status = ProviderOutreachStatus.Sent,
        };

        db.Suppliers.Add(supplier);
        db.DemandLeads.Add(lead);
        db.ProviderOutreaches.Add(row);
        await db.SaveChangesAsync();
        return (db, supplier, lead, row);
    }

    // ─── Feature 1: the bounce comes back ─────────────────────────────────────

    [Fact]
    public async Task HardBounce_MarksTheSupplierUnusable_AndFlipsTheOutreachRow()
    {
        var (db, supplier, _, row) = await ContactedProviderAsync();

        var result = await MakeWebhook(db, BouncePayload("dead@kolimine.ee"), "msg_1").Resend(default);

        result.Should().BeOfType<OkObjectResult>();
        var reloaded = await db.Suppliers.FindAsync(supplier.Id);
        reloaded!.ContactEmailUnusable.Should().BeTrue();
        reloaded.ContactEmailBounceType.Should().Be("hard");
        reloaded.ContactEmailBouncedAt.Should().NotBeNull();
        reloaded.ContactEmailBounceReason.Should().Contain("does not exist");

        var outreach = await db.ProviderOutreaches.FindAsync(row.Id);
        outreach!.Status.Should().Be(ProviderOutreachStatus.Bounced,
            "the workspace must stop claiming 'sent' for a mail that never arrived");
        outreach.Note.Should().Contain("Hard bounce");
    }

    [Fact]
    public async Task HardBounce_IsRecordedInTheAuditLogAndEventLedger()
    {
        var (db, supplier, _, _) = await ContactedProviderAsync();

        await MakeWebhook(db, BouncePayload("dead@kolimine.ee"), "msg_audit").Resend(default);

        var evt = db.EmailDeliveryEvents.Single();
        evt.EventId.Should().Be("msg_audit");
        evt.EventType.Should().Be("email.bounced");
        evt.Recipient.Should().Be("dead@kolimine.ee");
        evt.SupplierId.Should().Be(supplier.Id);
        evt.OutreachRowsUpdated.Should().Be(1);

        db.AuditLogs.Should().ContainSingle(a => a.Action == "email.bounced" && a.Actor == "resend-webhook");
    }

    [Fact]
    public async Task SoftBounce_DoesNotRetireTheAddress()
    {
        var (db, supplier, _, row) = await ContactedProviderAsync("full@kolimine.ee");

        await MakeWebhook(db, BouncePayload("full@kolimine.ee", "Transient"), "msg_soft").Resend(default);

        var reloaded = await db.Suppliers.FindAsync(supplier.Id);
        reloaded!.ContactEmailUnusable.Should().BeFalse(
            "a full mailbox today is not a dead business tomorrow");
        reloaded.ContactEmailBounceType.Should().Be("soft");
        reloaded.ContactEmailBouncedAt.Should().NotBeNull("ops still needs to see it happened");

        (await db.ProviderOutreaches.FindAsync(row.Id))!.Status
            .Should().Be(ProviderOutreachStatus.Bounced);
    }

    [Fact]
    public async Task Complaint_RetiresTheAddress_AndMarksTheRowComplained()
    {
        var (db, supplier, _, row) = await ContactedProviderAsync("angry@kolimine.ee");

        await MakeWebhook(db, ComplaintPayload("angry@kolimine.ee"), "msg_spam").Resend(default);

        (await db.Suppliers.FindAsync(supplier.Id))!.ContactEmailUnusable.Should().BeTrue();
        (await db.ProviderOutreaches.FindAsync(row.Id))!.Status
            .Should().Be(ProviderOutreachStatus.Complained);
    }

    [Fact]
    public async Task HardBounce_MarksEveryStillOpenRequestToThatAddress()
    {
        // The mailbox is gone, so every outstanding ask sent to it is gone too —
        // otherwise older leads keep showing a lie.
        var (db, supplier, _, first) = await ContactedProviderAsync();
        var second = new ProviderOutreach
        {
            Id = Guid.NewGuid(), DemandLeadId = Guid.NewGuid(), SupplierId = supplier.Id,
            SentTo = "dead@kolimine.ee", SentAt = DateTime.UtcNow.AddDays(-3),
            Status = ProviderOutreachStatus.Sent,
        };
        // An answered request keeps its human-set outcome.
        var replied = new ProviderOutreach
        {
            Id = Guid.NewGuid(), DemandLeadId = Guid.NewGuid(), SupplierId = supplier.Id,
            SentTo = "dead@kolimine.ee", SentAt = DateTime.UtcNow.AddDays(-9),
            Status = ProviderOutreachStatus.Replied,
        };
        db.ProviderOutreaches.AddRange(second, replied);
        await db.SaveChangesAsync();

        await MakeWebhook(db, BouncePayload("dead@kolimine.ee"), "msg_all").Resend(default);

        (await db.ProviderOutreaches.FindAsync(first.Id))!.Status.Should().Be(ProviderOutreachStatus.Bounced);
        (await db.ProviderOutreaches.FindAsync(second.Id))!.Status.Should().Be(ProviderOutreachStatus.Bounced);
        (await db.ProviderOutreaches.FindAsync(replied.Id))!.Status.Should().Be(ProviderOutreachStatus.Replied);
    }

    [Fact]
    public async Task SoftBounce_TouchesOnlyTheNewestOpenRequest()
    {
        var (db, supplier, _, newest) = await ContactedProviderAsync("full@kolimine.ee");
        var older = new ProviderOutreach
        {
            Id = Guid.NewGuid(), DemandLeadId = Guid.NewGuid(), SupplierId = supplier.Id,
            SentTo = "full@kolimine.ee", SentAt = DateTime.UtcNow.AddDays(-5),
            Status = ProviderOutreachStatus.Sent,
        };
        db.ProviderOutreaches.Add(older);
        await db.SaveChangesAsync();

        await MakeWebhook(db, BouncePayload("full@kolimine.ee", "Transient"), "msg_soft2").Resend(default);

        (await db.ProviderOutreaches.FindAsync(newest.Id))!.Status.Should().Be(ProviderOutreachStatus.Bounced);
        (await db.ProviderOutreaches.FindAsync(older.Id))!.Status.Should().Be(ProviderOutreachStatus.Sent);
    }

    [Fact]
    public async Task Redelivery_OfTheSameEvent_IsIdempotent()
    {
        var (db, supplier, _, row) = await ContactedProviderAsync();
        var payload = BouncePayload("dead@kolimine.ee");

        await MakeWebhook(db, payload, "msg_retry").Resend(default);
        // Ops corrects the row by hand between Resend's retries.
        (await db.ProviderOutreaches.FindAsync(row.Id))!.Status = ProviderOutreachStatus.NoAnswer;
        await db.SaveChangesAsync();

        var second = await MakeWebhook(db, payload, "msg_retry").Resend(default);

        second.Should().BeOfType<OkObjectResult>();
        db.EmailDeliveryEvents.Should().HaveCount(1, "the svix id is the idempotency key");
        db.AuditLogs.Count(a => a.Action == "email.bounced").Should().Be(1);
        (await db.ProviderOutreaches.FindAsync(row.Id))!.Status
            .Should().Be(ProviderOutreachStatus.NoAnswer, "a retry must not stomp the admin's edit");
    }

    [Fact]
    public async Task ForgedSignature_ChangesNothing_And401s()
    {
        var (db, supplier, _, row) = await ContactedProviderAsync();

        var result = await MakeWebhook(db, BouncePayload("dead@kolimine.ee"), "msg_forged",
            validSignature: false).Resend(default);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        (await db.Suppliers.FindAsync(supplier.Id))!.ContactEmailUnusable.Should().BeFalse();
        (await db.ProviderOutreaches.FindAsync(row.Id))!.Status.Should().Be(ProviderOutreachStatus.Sent);
        db.EmailDeliveryEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task ReplayedOldCapture_IsRejected()
    {
        var (db, _, _, _) = await ContactedProviderAsync();

        var result = await MakeWebhook(db, BouncePayload("dead@kolimine.ee"), "msg_replay",
            timestampOverride: DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds()).Resend(default);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        db.EmailDeliveryEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingWebhookSecret_FailsClosedWith503()
    {
        var (db, supplier, _, _) = await ContactedProviderAsync();

        var result = await MakeWebhook(db, BouncePayload("dead@kolimine.ee"), "msg_nosecret",
            secret: null).Resend(default);

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        (await db.Suppliers.FindAsync(supplier.Id))!.ContactEmailUnusable.Should().BeFalse();
    }

    [Fact]
    public async Task UnsubscribedEventType_IsAcceptedAndIgnored()
    {
        // Anything we did not ask for (clicked, sent, scheduled…) must be a
        // harmless 200 — turning an extra event on in the Resend dashboard
        // cannot be allowed to start failing deliveries in their retry queue.
        var (db, supplier, _, row) = await ContactedProviderAsync();
        const string clicked = """{"type":"email.clicked","data":{"to":["dead@kolimine.ee"]}}""";

        var result = await MakeWebhook(db, clicked, "msg_clicked").Resend(default);

        result.Should().BeOfType<OkObjectResult>();
        (await db.Suppliers.FindAsync(supplier.Id))!.ContactEmailUnusable.Should().BeFalse();
        var unchanged = await db.ProviderOutreaches.FindAsync(row.Id);
        unchanged!.Status.Should().Be(ProviderOutreachStatus.Sent);
        unchanged.DeliveredAt.Should().BeNull();
        unchanged.OpenedAt.Should().BeNull();
    }

    [Fact]
    public async Task BounceForAnAddressWeDoNotOwn_IsRecordedButHarmless()
    {
        var (db, supplier, _, row) = await ContactedProviderAsync();

        var result = await MakeWebhook(db, BouncePayload("someone.else@example.com"), "msg_unknown")
            .Resend(default);

        result.Should().BeOfType<OkObjectResult>();
        (await db.Suppliers.FindAsync(supplier.Id))!.ContactEmailUnusable.Should().BeFalse();
        (await db.ProviderOutreaches.FindAsync(row.Id))!.Status.Should().Be(ProviderOutreachStatus.Sent);
        db.EmailDeliveryEvents.Single().SupplierId.Should().BeNull();
    }

    [Fact]
    public async Task RecipientMatching_IsCaseInsensitive()
    {
        var (db, supplier, _, _) = await ContactedProviderAsync("Info@Kolimine.EE");

        await MakeWebhook(db, BouncePayload("info@kolimine.ee"), "msg_case").Resend(default);

        (await db.Suppliers.FindAsync(supplier.Id))!.ContactEmailUnusable.Should().BeTrue();
    }

    // ─── Delivery telemetry: sent → delivered → opened ────────────────────────
    //
    // Five Viljandi storage requests reached 18 providers and produced zero
    // replies of any kind. "Nobody answered" and "nothing arrived" were the same
    // stored fact, so these pin the receipts down — and, just as hard, pin down
    // everything a receipt must NOT be allowed to touch.

    [Fact]
    public async Task DeliveredEvent_StampsDeliveredAt_AndWritesNothingElse()
    {
        var (db, supplier, _, row) = await ContactedProviderAsync();
        var supplierUpdatedAt = supplier.UpdatedAt;

        var result = await MakeWebhook(db, DeliveredPayload("dead@kolimine.ee"), "msg_ok").Resend(default);

        result.Should().BeOfType<OkObjectResult>();
        var outreach = await db.ProviderOutreaches.FindAsync(row.Id);
        outreach!.DeliveredAt.Should().NotBeNull("the mail reaching a server is the fact we were missing");
        outreach.OpenedAt.Should().BeNull("delivered is not read");
        outreach.Status.Should().Be(ProviderOutreachStatus.Sent, "status carries the provider's intent, not the mail server's");
        outreach.Note.Should().BeNull("a receipt is not something ops needs to read in the workspace");

        var reloaded = await db.Suppliers.FindAsync(supplier.Id);
        reloaded!.ContactEmailUnusable.Should().BeFalse();
        reloaded.ContactEmailBouncedAt.Should().BeNull();
        reloaded.UpdatedAt.Should().Be(supplierUpdatedAt, "a delivery says nothing about the supplier record");

        db.EmailDeliveryEvents.Should().BeEmpty(
            "the ledger exists to explain retired addresses — one row per delivered email would bury them");
        db.AuditLogs.Should().BeEmpty(
            "a bounce is exceptional and worth an audit row; a delivery happens to every single email");
    }

    [Fact]
    public async Task OpenedEvent_StampsOpenedAt_AndDoesNotInferDelivery()
    {
        var (db, _, _, row) = await ContactedProviderAsync();

        await MakeWebhook(db, OpenedPayload("dead@kolimine.ee"), "msg_open").Resend(default);

        var outreach = await db.ProviderOutreaches.FindAsync(row.Id);
        outreach!.OpenedAt.Should().NotBeNull();
        outreach.DeliveredAt.Should().BeNull(
            "each column records what Resend actually reported, so 'delivered' stays checkable against their dashboard");
        outreach.Status.Should().Be(ProviderOutreachStatus.Sent);
    }

    [Theory]
    [InlineData(ProviderOutreachStatus.Replied)]
    [InlineData(ProviderOutreachStatus.NeedsInfo)]
    [InlineData(ProviderOutreachStatus.Declined)]
    [InlineData(ProviderOutreachStatus.NoAnswer)]
    public async Task Receipts_NeverOverwriteTheProvidersOwnStatus(ProviderOutreachStatus answered)
    {
        // Replied is the dangerous one: GetLeadMetrics counts it as a supplier
        // MATCH, the concierge north-star. A delivery receipt overwriting a real
        // reply would deflate that number silently and for good.
        var (db, _, _, row) = await ContactedProviderAsync();
        row.Status = answered;
        await db.SaveChangesAsync();

        await MakeWebhook(db, DeliveredPayload("dead@kolimine.ee"), "msg_st_d").Resend(default);
        await MakeWebhook(db, OpenedPayload("dead@kolimine.ee"), "msg_st_o").Resend(default);

        var outreach = await db.ProviderOutreaches.FindAsync(row.Id);
        outreach!.Status.Should().Be(answered, "the provider's intent outranks the mail server's receipt");
        outreach.DeliveredAt.Should().NotBeNull("an answered request still deserves an honest delivered count");
        outreach.OpenedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RedeliveredReceipt_ChangesNothing_AndKeepsTheFirstOpenTime()
    {
        var (db, _, _, row) = await ContactedProviderAsync();
        var firstAt = DateTime.UtcNow.AddMinutes(-1);

        await MakeWebhook(db, DeliveredPayload("dead@kolimine.ee", firstAt), "msg_r1").Resend(default);
        await MakeWebhook(db, OpenedPayload("dead@kolimine.ee", firstAt), "msg_r2").Resend(default);
        var afterFirst = await db.ProviderOutreaches.FindAsync(row.Id);
        var delivered  = afterFirst!.DeliveredAt;
        var opened     = afterFirst.OpenedAt;

        // Resend retries the identical event…
        await MakeWebhook(db, DeliveredPayload("dead@kolimine.ee", firstAt), "msg_r1").Resend(default);
        await MakeWebhook(db, OpenedPayload("dead@kolimine.ee", firstAt), "msg_r2").Resend(default);
        // …and the provider genuinely opens the mail again a day later, which is
        // a different event with a later timestamp, not a retry.
        var secondResult = await MakeWebhook(
            db, OpenedPayload("dead@kolimine.ee", firstAt.AddDays(1)), "msg_r3").Resend(default);

        secondResult.Should().BeOfType<OkObjectResult>();
        var outreach = await db.ProviderOutreaches.FindAsync(row.Id);
        outreach!.DeliveredAt.Should().Be(delivered, "first write wins");
        outreach.OpenedAt.Should().Be(opened,
            "OpenedAt answers 'was the ask seen while it was still actionable' — a later re-read must not move it");
    }

    [Fact]
    public async Task ReceiptForAnAddressWeNeverContacted_IsIgnoredWithoutThrowing()
    {
        // Customer acknowledgements, the intro campaign and every other mail we
        // send also produce receipts, and none of them has an outreach row.
        var (db, supplier, _, row) = await ContactedProviderAsync();

        var result = await MakeWebhook(db, DeliveredPayload("someone.else@example.com"), "msg_nobody")
            .Resend(default);

        result.Should().BeOfType<OkObjectResult>();
        var untouched = await db.ProviderOutreaches.FindAsync(row.Id);
        untouched!.DeliveredAt.Should().BeNull();
        untouched.Status.Should().Be(ProviderOutreachStatus.Sent);
        (await db.Suppliers.FindAsync(supplier.Id))!.ContactEmailUnusable.Should().BeFalse();
    }

    [Fact]
    public async Task Receipt_LandsOnTheRequestThatCouldHaveProducedIt()
    {
        // The same provider is asked about two different leads. Resend's email_id
        // is not stored on the row, so the address is the only join — but a mail
        // cannot be delivered before it was sent, and that bound is enough to
        // keep Monday's receipt off Tuesday's request.
        var (db, supplier, _, newest) = await ContactedProviderAsync();
        var earlierSentAt = DateTime.UtcNow.AddDays(-3);
        var older = new ProviderOutreach
        {
            Id = Guid.NewGuid(), DemandLeadId = Guid.NewGuid(), SupplierId = supplier.Id,
            SentTo = "dead@kolimine.ee", SentAt = earlierSentAt,
            Status = ProviderOutreachStatus.Sent,
        };
        db.ProviderOutreaches.Add(older);
        await db.SaveChangesAsync();

        await MakeWebhook(db, DeliveredPayload("dead@kolimine.ee", earlierSentAt.AddSeconds(5)), "msg_old")
            .Resend(default);

        (await db.ProviderOutreaches.FindAsync(older.Id))!.DeliveredAt.Should().NotBeNull();
        (await db.ProviderOutreaches.FindAsync(newest.Id))!.DeliveredAt.Should().BeNull(
            "a request sent two minutes ago cannot have been delivered three days ago");
    }

    [Fact]
    public async Task ReceiptAfterABounce_DoesNotResurrectTheAddress()
    {
        // A soft-bounced address that later delivers is normal (the mailbox was
        // full on Monday). The receipt records itself and nothing more — the
        // supplier's bounce history is ops' data, not the webhook's to revise.
        var (db, supplier, _, row) = await ContactedProviderAsync("full@kolimine.ee");
        await MakeWebhook(db, BouncePayload("full@kolimine.ee", "Transient"), "msg_b").Resend(default);

        await MakeWebhook(db, DeliveredPayload("full@kolimine.ee"), "msg_after").Resend(default);

        var reloaded = await db.Suppliers.FindAsync(supplier.Id);
        reloaded!.ContactEmailBouncedAt.Should().NotBeNull("the bounce still happened");
        reloaded.ContactEmailBounceType.Should().Be("soft");
        var outreach = await db.ProviderOutreaches.FindAsync(row.Id);
        outreach!.Status.Should().Be(ProviderOutreachStatus.Bounced, "a receipt never rewrites status");
        outreach.DeliveredAt.Should().NotBeNull();
    }

    // ─── Feature 1: auto fan-out routes around the dead address ───────────────

    [Fact]
    public async Task AutoFanOut_SkipsABouncedProvider_AndPicksTheNextCandidate()
    {
        var (db, nearest, next) = await TwoTartuProvidersAsync();
        nearest.ContactEmailUnusable = true;
        db.PlatformSettings.Add(new PlatformSetting { Key = "conciergeAutoOutreachMax", Value = "1" });
        var lead = TartuLead();
        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();

        var queue = new CapturingEmailQueue();
        var summary = await TestServices.Outreach(db, queue).AutoFanOutAsync(lead);

        summary.Emailed.Should().Be(1);
        queue.Recipients.Should().Equal(next.ContactEmail);
        db.ProviderOutreaches.Should().ContainSingle()
            .Which.SupplierId.Should().Be(next.Id, "the fan-out slot must go to a reachable provider");
    }

    [Fact]
    public async Task Outreach_SkipsABouncedProvider_WithAMachineReason()
    {
        var (db, supplier, lead, _) = await ContactedProviderAsync();
        supplier.ContactEmailUnusable = true;
        await db.SaveChangesAsync();

        var queue = new CapturingEmailQueue();
        var result = await TestServices.Outreach(db, queue)
            .SendAsync(lead, [supplier.Id], resend: true, actor: "admin");

        result.Sent.Should().BeEmpty();
        result.Skipped.Should().ContainSingle().Which.Reason.Should().Be("email_bounced");
        queue.Recipients.Should().BeEmpty("re-mailing a dead address only burns sending reputation");
    }

    [Fact]
    public async Task Candidates_ExposeTheUnreachableFlagToTheWorkspace()
    {
        var (db, nearest, _) = await TwoTartuProvidersAsync();
        nearest.ContactEmailUnusable = true;
        nearest.ContactEmailBouncedAt = DateTime.UtcNow;
        var lead = TartuLead();
        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();

        var candidates = await Ruumly.Backend.Helpers.ProviderCandidateFinder.SearchAsync(
            db, lead,
            new Ruumly.Backend.Helpers.ProviderCandidateSearch(null, false, false, 25, 50));

        candidates.Items.Single(c => c.SupplierId == nearest.Id)
            .ContactEmailUnusable.Should().BeTrue();
        candidates.Items.Single(c => c.SupplierId != nearest.Id)
            .ContactEmailUnusable.Should().BeFalse();
    }

    // ─── Feature 2: ops saves an address captured on a call ───────────────────

    [Fact]
    public async Task SavingANewContactEmail_ClearsTheBounceVerdict()
    {
        var (db, supplier, _, _) = await ContactedProviderAsync();
        await MakeWebhook(db, BouncePayload("dead@kolimine.ee"), "msg_fix").Resend(default);

        var result = await MakeSuppliers(db)
            .UpdateSupplier(supplier.Id, new UpdateSupplierRequest(ContactEmail: "kontakt@kolimine.ee"));

        result.Should().BeOfType<OkObjectResult>();
        var reloaded = await db.Suppliers.FindAsync(supplier.Id);
        reloaded!.ContactEmail.Should().Be("kontakt@kolimine.ee");
        reloaded.ContactEmailUnusable.Should().BeFalse("a new address deserves a fresh attempt");
        reloaded.ContactEmailBouncedAt.Should().BeNull();
        reloaded.ContactEmailBounceType.Should().BeNull();
        reloaded.ContactEmailBounceReason.Should().BeNull();
    }

    [Fact]
    public async Task ReSavingTheSameBouncedAddress_KeepsItRetired()
    {
        var (db, supplier, _, _) = await ContactedProviderAsync();
        await MakeWebhook(db, BouncePayload("dead@kolimine.ee"), "msg_same").Resend(default);

        await MakeSuppliers(db)
            .UpdateSupplier(supplier.Id, new UpdateSupplierRequest(ContactEmail: "DEAD@kolimine.ee"));

        (await db.Suppliers.FindAsync(supplier.Id))!.ContactEmailUnusable
            .Should().BeTrue("re-typing the same dead address does not resurrect it");
    }

    [Fact]
    public async Task SavingAMalformedEmail_IsRejected()
    {
        var (db, supplier, _, _) = await ContactedProviderAsync();

        var result = await MakeSuppliers(db)
            .UpdateSupplier(supplier.Id, new UpdateSupplierRequest(ContactEmail: "kontakt@"));

        result.Should().BeOfType<BadRequestObjectResult>();
        (await db.Suppliers.FindAsync(supplier.Id))!.ContactEmail.Should().Be("dead@kolimine.ee");
    }

    [Fact]
    public async Task SavingAnEmailOnAPhoneOnlyProvider_MakesItReachableAgain()
    {
        // The 127 "phone only" providers: no address at all until ops calls them.
        var (db, nearest, _) = await TwoTartuProvidersAsync();
        nearest.ContactEmail = "";
        var lead = TartuLead();
        db.DemandLeads.Add(lead);
        db.PlatformSettings.Add(new PlatformSetting { Key = "conciergeAutoOutreachMax", Value = "1" });
        await db.SaveChangesAsync();

        await MakeSuppliers(db)
            .UpdateSupplier(nearest.Id, new UpdateSupplierRequest(ContactEmail: "myyk@lahiladu.ee"));

        var queue = new CapturingEmailQueue();
        await TestServices.Outreach(db, queue).AutoFanOutAsync(lead);

        queue.Recipients.Should().Equal("myyk@lahiladu.ee");
    }

    // ─── Fixtures ─────────────────────────────────────────────────────────────

    private static DemandLead TartuLead() => new()
    {
        Id = Guid.NewGuid(), Email = "customer@example.ee", City = "Tartu",
        Category = DemandLeadCategory.Warehouse, Language = "et", Source = "concierge",
        Status = DemandLeadStatus.New,
    };

    /// <summary>Two warehouse providers in Tartu, the first measurably closer.</summary>
    private static async Task<(RuumlyDbContext Db, Supplier Nearest, Supplier Next)> TwoTartuProvidersAsync()
    {
        const double tartuLat = 58.377, tartuLng = 26.729, degreesPerKm = 1d / 111.195d;
        var db = TestDbContext.Create();

        Supplier Add(string name, string email, double offsetKm)
        {
            var supplier = new Supplier
            {
                Id = Guid.NewGuid(), Name = name, ContactName = name,
                ContactEmail = email, ContactPhone = "+372 555 0000", IsActive = true,
            };
            db.Suppliers.Add(supplier);
            db.SupplierLocations.Add(new SupplierLocation
            {
                Id = Guid.NewGuid(), SupplierId = supplier.Id, Name = $"{name} ladu",
                City = "Tartu", Address = "Riia 1", IsActive = true,
                Lat = tartuLat + offsetKm * degreesPerKm, Lng = tartuLng,
            });
            db.Listings.Add(new Listing
            {
                Id = Guid.NewGuid(), SupplierId = supplier.Id, Type = ListingType.Warehouse,
                Title = $"{name} boks", City = "Tartu", Address = "Riia 1",
                Lat = tartuLat + offsetKm * degreesPerKm, Lng = tartuLng,
                PriceFrom = 50m, PriceUnit = "month", IsActive = true,
            });
            return supplier;
        }

        var nearest = Add("Lähiladu OÜ", "info@lahiladu.ee", 0.5);
        var next    = Add("Teine Ladu OÜ", "info@teineladu.ee", 3.0);
        await db.SaveChangesAsync();
        return (db, nearest, next);
    }

    private static AdminSuppliersController MakeSuppliers(RuumlyDbContext db)
    {
        IDistributedCache cache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));

        // Only Db / pricing / cache are on the UpdateSupplier path; the rest of the
        // controller's dependencies are never touched by these tests.
        return new AdminSuppliersController(
            db,
            httpClientFactory: null!,
            NullLogger<AdminSuppliersController>.Instance,
            tokenProtector: null!,
            new PricingConfigService(db, cache),
            pollingService: null!,
            cache,
            new CapturingEmailQueue())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                        new Claim(ClaimTypes.Email, "admin@ruumly.eu"),
                        new Claim(ClaimTypes.Role, "Admin"),
                    ], "test")),
                },
            },
        };
    }
}
