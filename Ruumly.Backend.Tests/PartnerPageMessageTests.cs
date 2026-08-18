using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// A message written on a partner's public page (/{lang}/partner/{slug}) is
/// DEMAND, not correspondence: it must become a routed DemandLead and reach the
/// partner it was addressed to. Until 2026-08-17 it became one untracked ops
/// email and nothing else, while the dialog promised the sender a reply.
///
/// The other half of these tests is about who we are allowed to write to: the
/// directory is mostly scraped rows that never opted in, so delivery is gated,
/// and a partner-page message must NEVER trigger a fan-out to competitors.
/// </summary>
public class PartnerPageMessageTests
{
    private sealed class CapturingEmailQueue : IBackgroundEmailQueue
    {
        public List<(string To, string Subject, string TextBody, string? ReplyTo)> Emails { get; } = [];
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null)
            => Emails.Add((to, subject, textBody, null));
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody, string? replyTo)
            => Emails.Add((to, subject, textBody, replyTo));
        public void EnqueueVerificationEmail(Guid userId) { }
    }

    private sealed class CapturingNotifications : INotificationService
    {
        public List<(Guid UserId, string Title)> Created { get; } = [];
        public Task<PaginatedResult<NotificationDto>> GetAllAsync(Guid userId, int page = 1, int limit = 50)
            => Task.FromResult(new PaginatedResult<NotificationDto>([], 0, page, limit, false));
        public Task MarkReadAsync(Guid id, Guid userId) => Task.CompletedTask;
        public Task MarkAllReadAsync(Guid userId) => Task.CompletedTask;
        public Task CreateAsync(Guid userId, NotificationType type, string title, string desc,
            string? actionUrl = null, string? entityId = null, string? entityType = null)
        {
            Created.Add((userId, title));
            return Task.CompletedTask;
        }
    }

    private static SupportController MakeController(
        RuumlyDbContext db, IBackgroundEmailQueue queue, INotificationService notif) =>
        new(db, queue, notif, TestServices.Config(),
            TestServices.Outreach(db, queue),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SupportController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    /// <summary>
    /// A published storage partner in Peetri — the shape of the row the first real
    /// partner-page message landed on. Unclaimed unless a Provider user is added.
    /// </summary>
    private static Supplier SeedPartner(
        RuumlyDbContext db,
        string slug = "peetri-miniladu",
        string? serviceTypesJson = """["warehouse"]""",
        string contactEmail = "info@miniladu.ee",
        bool isActive = true,
        bool isPartnerPagePublished = true)
    {
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = "Peetri Miniladu", ContactName = "Owner",
            ContactEmail = contactEmail, ContactPhone = "+372 5000 0000",
            Slug = slug, IsPartnerPagePublished = isPartnerPagePublished, IsDirectoryListing = true,
            IsActive = isActive, ServiceTypesJson = serviceTypesJson,
        };
        db.Suppliers.Add(supplier);
        db.SupplierLocations.Add(new SupplierLocation
        {
            Id = Guid.NewGuid(), SupplierId = supplier.Id, Name = "Peetri ladu",
            Address = "Tähnase tee 1", City = "Peetri", Country = "EE",
            Lat = 59.39, Lng = 24.81, IsActive = true, IsSynthetic = false,
        });
        db.SaveChanges();
        return supplier;
    }

    /// <summary>Claiming a profile mints exactly this — see ClaimController.</summary>
    private static User Claim(RuumlyDbContext db, Supplier supplier)
    {
        var user = new User
        {
            Id = Guid.NewGuid(), Name = supplier.Name, Email = supplier.ContactEmail,
            Role = UserRole.Provider, SupplierId = supplier.Id,
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private static ContactRequest Message(string? slug, string message = "Do you have a 5 m² unit free in September?") =>
        new(Name: "Mari", Email: "mari@example.ee", Subject: "Storage question",
            Message: message, Language: "et", PartnerSlug: slug);

    private const string OpsInbox = "info@ruumly.eu";

    // ─── The lead ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PartnerMessage_CreatesLeadRoutedToThatPartner()
    {
        var db = TestDbContext.Create();
        var supplier = SeedPartner(db);
        var queue = new CapturingEmailQueue();

        var result = await MakeController(db, queue, new CapturingNotifications())
            .Contact(Message("peetri-miniladu"));

        result.Should().BeOfType<OkObjectResult>();

        var lead = db.DemandLeads.Single();
        lead.Source.Should().Be("partner-page",
            "its own tag keeps it out of the concierge north-star metrics and out of routed-quote counts");
        lead.SupplierId.Should().Be(supplier.Id);
        lead.Email.Should().Be("mari@example.ee");
        lead.Name.Should().Be("Mari");
        lead.Language.Should().Be("et");
        lead.Status.Should().Be(DemandLeadStatus.New);
        lead.Query.Should().Be("Do you have a 5 m² unit free in September?");
        lead.Category.Should().Be(DemandLeadCategory.Warehouse,
            "the partner sells exactly one consumer service, so that is what the message is about");
        lead.City.Should().Be("Peetri", "the visitor typed no city; the partner's own site is the honest stand-in");
    }

    [Fact]
    public async Task PartnerMessage_MultiServicePartner_LeadsWithAnyCategory()
    {
        var db = TestDbContext.Create();
        SeedPartner(db, serviceTypesJson: """["warehouse","moving","cleaning"]""");
        var queue = new CapturingEmailQueue();

        await MakeController(db, queue, new CapturingNotifications()).Contact(Message("peetri-miniladu"));

        db.DemandLeads.Single().Category.Should().Be(DemandLeadCategory.Any,
            "a partner selling three services says nothing about which one this message is; an admin routes it");
    }

    [Fact]
    public async Task PartnerMessage_LongMessage_IsClampedToTheColumn_AndKeptWholeInDetails()
    {
        var db = TestDbContext.Create();
        SeedPartner(db);
        var queue = new CapturingEmailQueue();
        var longMessage = new string('x', 1200);

        await MakeController(db, queue, new CapturingNotifications())
            .Contact(Message("peetri-miniladu", longMessage));

        var lead = db.DemandLeads.Single();
        lead.Query!.Length.Should().Be(500, "Query is the 500-char admin one-liner");
        lead.Details.Should().Be(longMessage, "the customer's own words survive whole where outreach reads them");
    }

    // ─── Who may be written to ───────────────────────────────────────────────

    [Fact]
    public async Task PartnerMessage_UnclaimedDirectoryRow_IsNeverEmailed()
    {
        var db = TestDbContext.Create();
        SeedPartner(db);                       // no Provider user → never claimed
        var queue = new CapturingEmailQueue();
        var notif = new CapturingNotifications();

        await MakeController(db, queue, notif).Contact(Message("peetri-miniladu"));

        queue.Emails.Should().ContainSingle(e => e.To == OpsInbox);
        queue.Emails.Should().NotContain(e => e.To == "info@miniladu.ee",
            "a scraped row never asked to hear from us — forwarding to it is a cold email on a stranger's behalf");
        notif.Created.Should().BeEmpty();

        var ops = queue.Emails.Single(e => e.To == OpsInbox).TextBody;
        ops.Should().Contain("Partner notified: NO (unclaimed directory profile)");
        ops.Should().Contain("RELAY THIS BY HAND");
        db.DemandLeads.Should().ContainSingle("the lead is still captured — only delivery is withheld");
    }

    [Fact]
    public async Task PartnerMessage_ClaimedPartner_IsEmailedAndNotified()
    {
        var db = TestDbContext.Create();
        var supplier = SeedPartner(db);
        var owner = Claim(db, supplier);
        var queue = new CapturingEmailQueue();
        var notif = new CapturingNotifications();

        await MakeController(db, queue, notif).Contact(Message("peetri-miniladu"));

        var toPartner = queue.Emails.Should().ContainSingle(e => e.To == "info@miniladu.ee").Which;
        toPartner.TextBody.Should().Contain("Do you have a 5 m² unit free in September?");
        toPartner.ReplyTo.Should().Be("mari@example.ee",
            "the page promised the partner would get back to them; reply must reach the customer");

        notif.Created.Should().ContainSingle(n => n.UserId == owner.Id);
        queue.Emails.Single(e => e.To == OpsInbox).TextBody
            .Should().Contain("Partner notified: yes");
    }

    [Fact]
    public async Task PartnerMessage_OptedOutPartner_IsNotEmailed_EvenWhenClaimed()
    {
        var db = TestDbContext.Create();
        var supplier = SeedPartner(db);
        Claim(db, supplier);
        supplier.MarketingOptOutAt = DateTime.UtcNow.AddDays(-1);
        supplier.MarketingOptOutReason = "REMOVE reply";
        db.SaveChanges();

        var queue = new CapturingEmailQueue();
        var notif = new CapturingNotifications();

        await MakeController(db, queue, notif).Contact(Message("peetri-miniladu"));

        queue.Emails.Should().NotContain(e => e.To == "info@miniladu.ee",
            "an opt-out is a promise we made in writing and outranks the claim");
        notif.Created.Should().BeEmpty();
        queue.Emails.Single(e => e.To == OpsInbox).TextBody
            .Should().Contain("Partner notified: NO (partner opted out of contact)");
    }

    [Fact]
    public async Task PartnerMessage_BouncedAddress_IsNotEmailed_EvenWhenClaimed()
    {
        var db = TestDbContext.Create();
        var supplier = SeedPartner(db);
        Claim(db, supplier);
        supplier.ContactEmailUnusable = true;
        supplier.ContactEmailBouncedAt = DateTime.UtcNow.AddDays(-2);
        supplier.ContactEmailBounceType = "hard";
        db.SaveChanges();

        var queue = new CapturingEmailQueue();

        await MakeController(db, queue, new CapturingNotifications()).Contact(Message("peetri-miniladu"));

        queue.Emails.Should().NotContain(e => e.To == "info@miniladu.ee",
            "a dead address cannot be reached and retrying only costs sending reputation");
        queue.Emails.Single(e => e.To == OpsInbox).TextBody
            .Should().Contain("Partner notified: NO (partner's email address has bounced)");
    }

    // ─── Never a fan-out ─────────────────────────────────────────────────────

    [Fact]
    public async Task PartnerMessage_NeverFansOutToOtherProviders()
    {
        var db = TestDbContext.Create();
        var supplier = SeedPartner(db);
        Claim(db, supplier);
        // A competitor that auto fan-out would happily have cold-emailed.
        var rival = new Supplier
        {
            Id = Guid.NewGuid(), Name = "Rival Ladu OÜ", ContactName = "R",
            ContactEmail = "sales@rival.ee", ContactPhone = "+372 5", IsActive = true,
            ServiceTypesJson = """["warehouse"]""",
        };
        db.Suppliers.Add(rival);
        db.SupplierLocations.Add(new SupplierLocation
        {
            Id = Guid.NewGuid(), SupplierId = rival.Id, Name = "Rival ladu",
            Address = "Peetri tee 2", City = "Peetri", Country = "EE",
            Lat = 59.39, Lng = 24.81, IsActive = true,
        });
        db.SaveChanges();

        var queue = new CapturingEmailQueue();

        await MakeController(db, queue, new CapturingNotifications()).Contact(Message("peetri-miniladu"));

        queue.Emails.Should().NotContain(e => e.To == "sales@rival.ee",
            "the visitor addressed ONE company; mailing its competitors off that note would be indefensible");
        db.ProviderOutreaches.Should().BeEmpty("no fan-out means no outreach rows and no quote tokens minted");
        queue.Emails.Select(e => e.To).Should().BeEquivalentTo(new[] { OpsInbox, "info@miniladu.ee" });
    }

    // ─── Degrading safely ────────────────────────────────────────────────────

    [Fact]
    public async Task PartnerMessage_UnknownSlug_DegradesToOpsEmailOnly()
    {
        var db = TestDbContext.Create();
        SeedPartner(db);
        var queue = new CapturingEmailQueue();

        var result = await MakeController(db, queue, new CapturingNotifications())
            .Contact(Message("no-such-partner"));

        result.Should().BeOfType<OkObjectResult>("a directory row going away must never surface as an error");
        db.DemandLeads.Should().BeEmpty();
        queue.Emails.Should().ContainSingle(e => e.To == OpsInbox)
            .Which.TextBody.Should().Contain("matched no active partner");
    }

    [Fact]
    public async Task PartnerMessage_InactivePartner_DegradesToOpsEmailOnly()
    {
        var db = TestDbContext.Create();
        SeedPartner(db, isActive: false);
        var queue = new CapturingEmailQueue();

        var result = await MakeController(db, queue, new CapturingNotifications())
            .Contact(Message("peetri-miniladu"));

        result.Should().BeOfType<OkObjectResult>();
        db.DemandLeads.Should().BeEmpty();
        queue.Emails.Should().ContainSingle(e => e.To == OpsInbox);
    }

    /// <summary>
    /// The public profile endpoint serves a partner only when it is active AND
    /// published, so an unpublished row has no page a visitor could have written
    /// from. Capturing a lead for one would mean trusting a slug that reached us
    /// some other way — and the whole point of sending the slug instead of prose
    /// is that it decides which supplier a message is routed to.
    /// </summary>
    [Fact]
    public async Task PartnerMessage_UnpublishedPartner_DegradesToOpsEmailOnly()
    {
        var db = TestDbContext.Create();
        SeedPartner(db, isPartnerPagePublished: false);
        var queue = new CapturingEmailQueue();

        var result = await MakeController(db, queue, new CapturingNotifications())
            .Contact(Message("peetri-miniladu"));

        result.Should().BeOfType<OkObjectResult>();
        db.DemandLeads.Should().BeEmpty();
        queue.Emails.Should().ContainSingle(e => e.To == OpsInbox);
    }

    [Fact]
    public async Task PartnerMessage_MalformedSlug_IsNotQueried_AndDegrades()
    {
        var db = TestDbContext.Create();
        SeedPartner(db);
        var queue = new CapturingEmailQueue();

        var result = await MakeController(db, queue, new CapturingNotifications())
            .Contact(Message("../Admin OR 1=1"));

        result.Should().BeOfType<OkObjectResult>();
        db.DemandLeads.Should().BeEmpty();
    }

    // ─── The plain contact form is untouched ─────────────────────────────────

    [Fact]
    public async Task Contact_WithoutPartnerSlug_IsByteForByteWhatItWas()
    {
        var db = TestDbContext.Create();
        SeedPartner(db);
        var queue = new CapturingEmailQueue();
        var notif = new CapturingNotifications();

        var result = await MakeController(db, queue, notif).Contact(
            new ContactRequest("Jane", "jane@test.ee", "Need storage", "Looking for a unit in Tallinn.", "et"));

        result.Should().BeOfType<OkObjectResult>();
        db.DemandLeads.Should().BeEmpty("the generic contact page is not demand capture");
        notif.Created.Should().BeEmpty();

        var mail = queue.Emails.Should().ContainSingle().Which;
        mail.To.Should().Be(OpsInbox);
        mail.Subject.Should().Be("[Ruumly contact] Need storage");
        mail.TextBody.Should().Be(
            "From: Jane <jane@test.ee>\nLang: et\n\nLooking for a unit in Tallinn.\n\n— Reply directly to jane@test.ee");
    }
}
