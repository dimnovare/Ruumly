using System.Security.Claims;
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
/// Concierge pivot — demand-first intake and admin ops. A public visitor
/// describes what they need (POST /api/leads/request) without picking a
/// listing; the admin works the lead (ContactedAt stamped on first status
/// move), asks for match suggestions, and tracks the funnel via /leads/metrics.
/// </summary>
public class ConciergeLeadTests
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

    private static SupportController MakeSupport(
        RuumlyDbContext db, IBackgroundEmailQueue queue, IConciergeOutreachService? outreach = null) =>
        new(db, queue, new NoOpNotifications(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            outreach ?? TestServices.Outreach(db, queue),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SupportController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static AdminLeadsController MakeAdmin(RuumlyDbContext db) =>
        new(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                        new Claim(ClaimTypes.Role, "Admin"),
                    ], "test")),
                },
            },
        };

    private static object? Prop(object o, string name) =>
        o.GetType().GetProperty(name)!.GetValue(o);

    // ─── Public concierge intake ──────────────────────────────────────────────

    [Fact]
    public async Task RequestConcierge_MultiCategory_SavesAnyLead_WithMachineQuery_AndEmailsAdmin()
    {
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();

        var result = await MakeSupport(db, queue).RequestConcierge(new ConciergeRequest(
            Email: "cust@x.ee", City: "Tallinn", Name: "Cust", Phone: "+372 5",
            Categories: ["Moving", "warehouse"], ToCity: "Tartu",
            // Deliberately Kind=Unspecified — exactly what System.Text.Json produces for a
            // bare "2026-08-15" body value; the controller must normalize to UTC, otherwise
            // Npgsql rejects the write to the timestamptz column in production.
            NeedDate: new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Unspecified),
            Details: "2-room flat plus some pallets", Language: "en"));

        result.Should().BeOfType<OkObjectResult>();

        var lead = db.DemandLeads.Single();
        lead.Category.Should().Be(DemandLeadCategory.Any, "more than one valid category falls back to Any");
        lead.Source.Should().Be("concierge");
        lead.Status.Should().Be(DemandLeadStatus.New);
        lead.City.Should().Be("Tallinn");
        lead.ToCity.Should().Be("Tartu");
        lead.NeedDate.Should().NotBeNull();
        lead.NeedDate!.Value.Kind.Should().Be(DateTimeKind.Utc, "Unspecified kinds must be normalized before hitting timestamptz");
        lead.Details.Should().Be("2-room flat plus some pallets");
        lead.Language.Should().Be("en");

        // Compact ENGLISH machine summary — categories + route, never translated labels.
        lead.Query.Should().StartWith("concierge:");
        lead.Query.Should().Contain("moving");
        lead.Query.Should().Contain("warehouse");
        lead.Query.Should().Contain("Tallinn");
        lead.Query.Should().Contain("2026-08-15");

        queue.Emails.Should().ContainSingle(e => e.To == "info@ruumly.eu",
            "an unrouted concierge lead alerts the unified ops inbox (opsInbox, default info@)");
    }

    [Fact]
    public async Task RequestConcierge_SingleCategory_MapsToThatEnum()
    {
        var db = TestDbContext.Create();

        var result = await MakeSupport(db, new CapturingEmailQueue()).RequestConcierge(
            new ConciergeRequest(Email: "cust@x.ee", City: "Tallinn", Categories: ["MOVING"]));

        result.Should().BeOfType<OkObjectResult>();
        db.DemandLeads.Single().Category.Should().Be(DemandLeadCategory.Moving);
    }

    // ─── Retired consumer categories: packing + insurance (2026-08) ───────────
    // Market research across EE/LV/LT: packing is NEVER sold standalone in the
    // Baltics — it is a line item inside a moving company's offer; and "insurance"
    // here means CMR carrier liability sold B2B to hauliers, not something a
    // household buys. Neither may create a top-level lead in its own category
    // (nobody to route it to), but the ask must never be silently discarded.

    [Fact]
    public async Task RequestConcierge_PackingAlone_CreatesMovingLead_AndKeepsThePackingAskVisible()
    {
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();

        SeedMovingProvider(db, "Tallinn Movers", "m@x.ee");
        await db.SaveChangesAsync();

        var result = await MakeSupport(db, queue).RequestConcierge(new ConciergeRequest(
            Email: "cust@x.ee", City: "Tallinn", Categories: ["Packing"],
            Details: "2-room flat, 3rd floor"));

        result.Should().BeOfType<OkObjectResult>();

        var lead = db.DemandLeads.Single();
        lead.Category.Should().Be(DemandLeadCategory.Moving,
            "packing is not a standalone business here — it is priced inside a mover's offer");

        // The admin-facing signal lives in the English machine summary, which is
        // what the admin list view shows.
        lead.Query.Should().Contain("moving", "the routed category leads the machine summary");
        lead.Query.Should().Contain("packing", "the admin must still see that packing help was asked for");

        // ...and NOT in Details. That field is printed verbatim into a cold email
        // written in the PROVIDER's language, so an English ops note in brackets
        // would land in the middle of an Estonian mail and read like spam.
        lead.Details.Should().Be("2-room flat, 3rd floor",
            "Details carries the customer's own words and nothing else");
        lead.Details.Should().NotContain("Packing help requested");
        lead.Details.Should().NotContain("[");

        queue.Emails.Single(e => e.To == "info@ruumly.eu").TextBody
            .Should().Contain("packing (routed as: moving)",
                "the ops alert reports what was asked AND how it was routed");

        // A moving lead fans out normally, and the mover we ask for a price sees
        // that packing is part of the job — in Estonian, because the seeded
        // supplier is an EE company.
        const string packingEstonian =
            "Klient soovib lisaks pakkimisabi — palun arvestage see oma hinna sisse.";
        var providerEmail = ProviderEmails(queue).Should().ContainSingle().Subject;
        providerEmail.TextBody.Should().Contain(packingEstonian);
        providerEmail.HtmlBody.Should().Contain(packingEstonian);
        providerEmail.TextBody.Should().NotContain("Packing help requested",
            "the provider must never receive the English ops note");
    }

    [Fact]
    public async Task RequestConcierge_InsuranceAlone_FallsBackToAny_NeverAnInsuranceLead()
    {
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();

        SeedMovingProvider(db, "Tallinn Movers", "m@x.ee");
        await db.SaveChangesAsync();

        var result = await MakeSupport(db, queue).RequestConcierge(new ConciergeRequest(
            Email: "cust@x.ee", City: "Tallinn", Categories: ["insurance"]));

        result.Should().BeOfType<OkObjectResult>();

        var lead = db.DemandLeads.Single();
        lead.Category.Should().Be(DemandLeadCategory.Any,
            "there is no consumer insurance product to route to — an admin decides by hand");
        lead.Category.Should().NotBe(DemandLeadCategory.Insurance);

        // Written down where the admin works — never in Details. "no consumer
        // product; route by hand" is an instruction to US, and Details is printed
        // verbatim into provider mail.
        lead.Query.Should().Contain("insurance",
            "the request is routed by hand, so what was asked must be written down");
        lead.Details.Should().BeNull("the visitor typed no details of their own");

        queue.Emails.Single(e => e.To == "info@ruumly.eu").TextBody
            .Should().Contain("insurance (routed as: any)");
        ProviderEmails(queue).Should().BeEmpty("an Any lead never blasts providers");
    }

    [Fact]
    public async Task RequestConcierge_MovingPlusPacking_StillCreatesAMovingLead()
    {
        var db = TestDbContext.Create();

        var result = await MakeSupport(db, new CapturingEmailQueue()).RequestConcierge(
            new ConciergeRequest(Email: "cust@x.ee", City: "Tallinn",
                Categories: ["moving", "packing"]));

        result.Should().BeOfType<OkObjectResult>();

        var lead = db.DemandLeads.Single();
        lead.Category.Should().Be(DemandLeadCategory.Moving,
            "packing resolves to moving, so the pair is ONE category — not the Any fallback");
        lead.Query.Should().Contain("packing", "the add-on is still recorded");
    }

    // Regression guard for the retained enum members. New leads are never created
    // in Packing/Insurance any more, but production rows already carry them and the
    // Category column persists the enum NAME — deleting a member would make those
    // rows unreadable. Every read path an admin uses must keep working.
    [Fact]
    public async Task PreExistingPackingAndInsuranceLeads_AreStillReadable()
    {
        var db  = TestDbContext.Create();
        var now = DateTime.UtcNow;

        DemandLead Legacy(DemandLeadCategory category) => new()
        {
            Id = Guid.NewGuid(), Email = "old@x.ee", City = "Tallinn",
            Category = category, Language = "et", Source = "concierge",
            Status = DemandLeadStatus.New, CreatedAt = now.AddDays(-30),
        };

        db.DemandLeads.AddRange(Legacy(DemandLeadCategory.Packing), Legacy(DemandLeadCategory.Insurance));
        await db.SaveChangesAsync();

        var admin = MakeAdmin(db);

        var (total, items) = ReadLeads(await admin.GetLeads(
            status: null, source: null, category: null, city: null));
        total.Should().Be(2, "historical leads must not vanish from the admin queue");
        items.Select(i => Prop(i, "category"))
             .Should().BeEquivalentTo(new[] { "packing", "insurance" });

        // The category filter still resolves both slugs (they remain in the storage
        // catalogue even though nobody can select them any more).
        ReadLeads(await admin.GetLeads(status: null, source: null, category: "packing", city: null))
            .Total.Should().Be(1);
        ReadLeads(await admin.GetLeads(status: null, source: null, category: "insurance", city: null))
            .Total.Should().Be(1);

        // Match suggestions and localized labels must not throw on them either.
        foreach (var lead in db.DemandLeads.ToList())
        {
            (await admin.GetLeadMatches(lead.Id)).Should().BeOfType<OkObjectResult>();
            Ruumly.Backend.Constants.ServiceCategories.SlugFor(lead.Category)
                .Should().BeOneOf("packing", "insurance");
            foreach (var lang in new[] { "et", "en", "ru", "lv", "lt" })
                Ruumly.Backend.Helpers.EmailTranslations.For(lang)
                    .CategoryLabel(lead.Category).Should().NotBeNullOrWhiteSpace();
        }
    }

    // ─── Multi-service requests reach providers (2026-08-13) ─────────────────
    // Step 1 of the intake says "pick everything you need", and until now doing
    // exactly that was the one thing guaranteed to reach NOBODY: two services do
    // not fit one Category column, the lead landed on Any, and the fan-out bailed
    // out and waited for someone to open the workspace. At roughly five qualified
    // requests a month, the customers who followed our own instructions were the
    // ones left uncontacted.
    //
    // The selection is recovered from the Query machine summary the intake
    // already writes, and each service is searched on its own — the Any wildcard
    // is never handed to the finder, which treats it as "every supplier matches".

    [Fact]
    public async Task RequestConcierge_MultiService_ContactsProvidersOfEachServiceAsked()
    {
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();

        var mover   = SeedMovingProvider(db, "Tallinn Movers", "mover@x.ee");
        var storage = SeedWarehouseProvider(db, "Tallinn Storage", "storage@x.ee");
        // The control: a perfectly good provider of something nobody asked for.
        var cleaner = SeedServiceProvider(db, "Tallinn Cleaners", "cleaner@x.ee", ["cleaning"]);
        await db.SaveChangesAsync();

        var result = await MakeSupport(db, queue).RequestConcierge(new ConciergeRequest(
            Email: "cust@x.ee", City: "Tallinn", Categories: ["moving", "warehouse"],
            Details: "2-room flat plus some pallets"));

        result.Should().BeOfType<OkObjectResult>();

        var contacted = db.ProviderOutreaches.Select(o => o.SupplierId).ToList();
        contacted.Should().Contain(mover.Id, "they asked to be moved");
        contacted.Should().Contain(storage.Id, "and they asked for storage too");
        contacted.Should().NotContain(cleaner.Id,
            "fanning out on the wildcard would blast the whole directory — the exact "
            + "cold-contact burn the old skip existed to prevent");

        ProviderEmails(queue).Select(e => e.To)
            .Should().BeEquivalentTo(["mover@x.ee", "storage@x.ee"]);
    }

    [Fact]
    public async Task RequestConcierge_MultiService_LeavesTheLeadOnAny()
    {
        // The fan-out steers the provider search per service with a DETACHED copy
        // of the lead. Assigning the tracked entity's Category to search would
        // persist one arbitrary service onto the customer's request and quietly
        // rewrite what they asked for.
        var db = TestDbContext.Create();
        SeedMovingProvider(db, "Tallinn Movers", "mover@x.ee");
        SeedWarehouseProvider(db, "Tallinn Storage", "storage@x.ee");
        await db.SaveChangesAsync();

        await MakeSupport(db, new CapturingEmailQueue()).RequestConcierge(new ConciergeRequest(
            Email: "cust@x.ee", City: "Tallinn", Categories: ["moving", "warehouse"]));

        db.DemandLeads.Single().Category.Should().Be(DemandLeadCategory.Any,
            "the lead still describes a request for several services");
    }

    [Fact]
    public async Task RequestConcierge_MultiService_SpreadsTheQuotaAcrossServices()
    {
        // The cap is shared, so a service with more supply must not eat it. Three
        // movers and one storage provider with room for two contacts has to reach
        // one of each: spending both slots on movers would leave the storage half
        // of the customer's move unquoted, which is the same silent failure.
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();

        SeedMovingProvider(db, "Movers A", "a@x.ee");
        SeedMovingProvider(db, "Movers B", "b@x.ee");
        SeedMovingProvider(db, "Movers C", "c@x.ee");
        var storage = SeedWarehouseProvider(db, "Tallinn Storage", "storage@x.ee");
        SetSetting(db, "conciergeAutoOutreachMax", "2");
        await db.SaveChangesAsync();

        await MakeSupport(db, queue).RequestConcierge(new ConciergeRequest(
            Email: "cust@x.ee", City: "Tallinn", Categories: ["moving", "warehouse"]));

        var contacted = db.ProviderOutreaches.Select(o => o.SupplierId).ToList();
        contacted.Should().HaveCount(2, "the quota is a total, not per service");
        contacted.Should().Contain(storage.Id, "the scarcer service must still get its slot");
    }

    [Fact]
    public async Task RequestConcierge_MultiService_ContactsADualServiceProviderOnce()
    {
        // A mover that also rents storage matches both searches. Two emails about
        // one job spends two of their cold contacts and reads as a mistake.
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();

        var both = SeedServiceProvider(db, "Movers & Storage", "both@x.ee", ["moving", "warehouse"]);
        await db.SaveChangesAsync();

        await MakeSupport(db, queue).RequestConcierge(new ConciergeRequest(
            Email: "cust@x.ee", City: "Tallinn", Categories: ["moving", "warehouse"]));

        db.ProviderOutreaches.Should().ContainSingle().Which.SupplierId.Should().Be(both.Id);
        ProviderEmails(queue).Should().ContainSingle();
    }

    [Fact]
    public async Task RequestConcierge_NoRoutableService_StillWaitsForAnAdmin()
    {
        // The deliberate skip survives for the case it was written for: nothing
        // routable was asked, so there is no specific question to put to anyone.
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();

        SeedMovingProvider(db, "Tallinn Movers", "mover@x.ee");
        SeedWarehouseProvider(db, "Tallinn Storage", "storage@x.ee");
        await db.SaveChangesAsync();

        await MakeSupport(db, queue).RequestConcierge(new ConciergeRequest(
            Email: "cust@x.ee", City: "Tallinn", Categories: ["insurance"]));

        db.ProviderOutreaches.Should().BeEmpty();
        ProviderEmails(queue).Should().BeEmpty("there is nothing specific to ask for");
        queue.Emails.Single(e => e.To == "info@ruumly.eu").TextBody
            .Should().Contain("names no service we can route",
                "the ops alert must say the lead still needs hand-work");
    }

    [Fact]
    public async Task RequestConcierge_MultiServicePlusPacking_TreatsTheMarkerAsAnAddOn()
    {
        // "warehouse"+"packing" resolves to warehouse+moving and the intake also
        // stamps a +packing-addon marker into the same Query segment the fan-out
        // now reads. The marker is an add-on note, never a service to go shopping
        // for — and it must not stop the two real ones from being contacted.
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();

        var mover   = SeedMovingProvider(db, "Tallinn Movers", "mover@x.ee");
        var storage = SeedWarehouseProvider(db, "Tallinn Storage", "storage@x.ee");
        await db.SaveChangesAsync();

        await MakeSupport(db, queue).RequestConcierge(new ConciergeRequest(
            Email: "cust@x.ee", City: "Tallinn", Categories: ["warehouse", "packing"]));

        db.DemandLeads.Single().Query.Should().Contain("+packing-addon");
        db.ProviderOutreaches.Select(o => o.SupplierId)
            .Should().BeEquivalentTo([storage.Id, mover.Id]);
    }

    [Fact]
    public async Task RequestConcierge_MultiService_TellsOpsWhoWasContacted()
    {
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();

        SeedMovingProvider(db, "Tallinn Movers", "mover@x.ee");
        SeedWarehouseProvider(db, "Tallinn Storage", "storage@x.ee");
        await db.SaveChangesAsync();

        await MakeSupport(db, queue).RequestConcierge(new ConciergeRequest(
            Email: "cust@x.ee", City: "Tallinn", Categories: ["moving", "warehouse"]));

        var alert = queue.Emails.Single(e => e.To == "info@ruumly.eu").TextBody;
        alert.Should().Contain("Auto-contacted: 2 provider(s)");
        alert.Should().Contain("Tallinn Movers").And.Contain("Tallinn Storage");
    }

    [Fact]
    public async Task RequestConcierge_MissingEmailOrCity_400_NoLead()
    {
        var db      = TestDbContext.Create();
        var support = MakeSupport(db, new CapturingEmailQueue());

        var noEmail = await support.RequestConcierge(new ConciergeRequest(Email: "not-an-email", City: "Tallinn"));
        noEmail.Should().BeOfType<BadRequestObjectResult>();

        var noCity = await support.RequestConcierge(new ConciergeRequest(Email: "c@x.ee", City: "  "));
        noCity.Should().BeOfType<BadRequestObjectResult>();

        db.DemandLeads.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestConcierge_UnknownLanguage_FallsBackToEt()
    {
        var db = TestDbContext.Create();

        await MakeSupport(db, new CapturingEmailQueue()).RequestConcierge(
            new ConciergeRequest(Email: "c@x.ee", City: "Tallinn", Language: "xx"));

        db.DemandLeads.Single().Language.Should().Be("et");
    }

    // A1 — enriched instant ops alert: a one-click workspace deep link + how many
    // providers we could reach right now (nearby, 25 km). Since the auto-fanout
    // landed, intake queues TWO kinds of mail — the ops alert and one provider
    // outreach per contacted provider — so each is asserted by recipient.
    [Fact]
    public async Task RequestConcierge_OpsAlert_IncludesWorkspaceDeepLink_AndNearbyProviderCount()
    {
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();

        // One active same-city provider that can serve the lead's category.
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = "Tallinn Movers", ContactName = "C",
            ContactEmail = "m@x.ee", ContactPhone = "1", IsActive = true,
        };
        db.Suppliers.Add(supplier);
        db.Listings.Add(new Listing
        {
            Id = Guid.NewGuid(), SupplierId = supplier.Id, Type = ListingType.Moving,
            Title = "Moving in Tallinn", City = "Tallinn", IsActive = true,
            PriceFrom = 50m, PriceUnit = "onetime", UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        await MakeSupport(db, queue).RequestConcierge(new ConciergeRequest(
            Email: "cust@x.ee", City: "Tallinn", Categories: ["moving"]));

        var email  = queue.Emails.Should().ContainSingle(e => e.To == "info@ruumly.eu").Subject;
        var leadId = db.DemandLeads.Single().Id;
        email.Subject.Should().Be("New concierge request — Tallinn");
        email.TextBody.Should().Contain("1 providers within 25 km",
            "the alert tells ops how many providers are reachable right now");
        email.TextBody.Should().Contain($"admin?tab=leads&lead={leadId}",
            "the alert deep-links into the lead's workspace");
        email.TextBody.Should().Contain("Auto-contacted: 1 provider(s) within 25 km — Tallinn Movers",
            "the alert reports what the auto-fanout did, so ops knows whether hand-work is still needed");

        // The second message intake now queues: the availability request the
        // auto-fanout sent to that provider.
        var outreach = ProviderEmails(queue).Should().ContainSingle().Subject;
        outreach.To.Should().Be("m@x.ee");
        outreach.Subject.Should().Contain("Ruumly");
        outreach.ReplyTo.Should().Be("info@ruumly.eu");
    }

    // ─── Auto-fanout on intake ────────────────────────────────────────────────
    // Live data: 10 leads, 8 provider emails EVER sent, one lead contacted 13 h
    // late and one never — while the single batch that did go out got a 75 %
    // reply rate. Outreach must not wait for an admin to open the workspace.

    private static Supplier SeedMovingProvider(
        RuumlyDbContext db, string name, string? contactEmail, string city = "Tallinn")
    {
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = name, ContactName = "C",
            ContactEmail = contactEmail ?? "", ContactPhone = "1", IsActive = true,
        };
        db.Suppliers.Add(supplier);
        db.Listings.Add(new Listing
        {
            Id = Guid.NewGuid(), SupplierId = supplier.Id, Supplier = supplier,
            Type = ListingType.Moving, Title = $"Moving — {name}", City = city,
            IsActive = true, PriceFrom = 50m, PriceUnit = "onetime", UpdatedAt = DateTime.UtcNow,
        });
        return supplier;
    }

    private static Supplier SeedWarehouseProvider(
        RuumlyDbContext db, string name, string? contactEmail, string city = "Tallinn")
    {
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = name, ContactName = "C",
            ContactEmail = contactEmail ?? "", ContactPhone = "1", IsActive = true,
        };
        db.Suppliers.Add(supplier);
        db.Listings.Add(new Listing
        {
            Id = Guid.NewGuid(), SupplierId = supplier.Id, Supplier = supplier,
            Type = ListingType.Warehouse, Title = $"Storage — {name}", City = city,
            IsActive = true, PriceFrom = 40m, PriceUnit = "month", UpdatedAt = DateTime.UtcNow,
        });
        return supplier;
    }

    /// <summary>
    /// A provider whose capability is DECLARED rather than listed — cleaning and
    /// van rental have no ListingType, so ServiceTypesJson plus a location is the
    /// only way they are ever matched (see ProviderCandidateFinder.MatchesCategory).
    /// </summary>
    private static Supplier SeedServiceProvider(
        RuumlyDbContext db, string name, string? contactEmail, string[] serviceTypes,
        string city = "Tallinn")
    {
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = name, ContactName = "C",
            ContactEmail = contactEmail ?? "", ContactPhone = "1", IsActive = true,
            ServiceTypesJson = System.Text.Json.JsonSerializer.Serialize(serviceTypes),
        };
        db.Suppliers.Add(supplier);
        db.SupplierLocations.Add(new SupplierLocation
        {
            Id = Guid.NewGuid(), SupplierId = supplier.Id, Supplier = supplier,
            Name = name, Address = city, City = city, Lat = 59.437, Lng = 24.753,
            IsActive = true,
        });
        return supplier;
    }

    private static void SetSetting(RuumlyDbContext db, string key, string value) =>
        db.PlatformSettings.Add(new PlatformSetting { Key = key, Value = value });

    private static List<(string To, string Subject, string TextBody, string? HtmlBody, string? ReplyTo)>
        ProviderEmails(CapturingEmailQueue queue) =>
        // "Everything not addressed to ops" stopped meaning "a provider" on
        // 2026-08-13, when the intake started sending the CUSTOMER their own
        // acknowledgement. Excluding the customer's own address keeps this
        // helper meaning what its name says — and keeps the identity-leak test
        // below honest, since that mail is allowed to contain their name and
        // number precisely because it is addressed to them.
        queue.Emails
             .Where(e => e.To != "info@ruumly.eu")
             .Where(e => !CustomerAddresses.Contains(e.To))
             .ToList();

    /// <summary>Addresses these tests submit requests from — never providers.</summary>
    private static readonly HashSet<string> CustomerAddresses = new(StringComparer.OrdinalIgnoreCase)
    {
        "cust@x.ee", "new@x.ee", "mari.maasikas@example.com",
    };

    [Fact]
    public async Task RequestConcierge_AutoFanout_EmailsAtMostTheConfiguredMax()
    {
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();

        for (var i = 0; i < 8; i++)
            SeedMovingProvider(db, $"Mover {i}", $"mover{i}@x.ee");
        SetSetting(db, "conciergeAutoOutreachMax", "3");
        await db.SaveChangesAsync();

        var result = await MakeSupport(db, queue).RequestConcierge(new ConciergeRequest(
            Email: "cust@x.ee", City: "Tallinn", Categories: ["moving"]));

        result.Should().BeOfType<OkObjectResult>();
        ProviderEmails(queue).Should().HaveCount(3, "conciergeAutoOutreachMax caps one lead's fanout");
        db.ProviderOutreaches.Should().HaveCount(3, "one row per email, minted with its own quote token");
        db.ProviderOutreaches.Select(o => o.QuoteToken).Should().OnlyHaveUniqueItems();
        db.DemandLeads.Single().Status.Should().Be(DemandLeadStatus.Contacted,
            "the first outreach is the first touch");
        db.DemandLeads.Single().ContactedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RequestConcierge_AutoFanout_ClampsMaxToTwelve()
    {
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();

        for (var i = 0; i < 15; i++)
            SeedMovingProvider(db, $"Mover {i}", $"mover{i}@x.ee");
        SetSetting(db, "conciergeAutoOutreachMax", "99");
        await db.SaveChangesAsync();

        await MakeSupport(db, queue).RequestConcierge(new ConciergeRequest(
            Email: "cust@x.ee", City: "Tallinn", Categories: ["moving"]));

        ProviderEmails(queue).Should().HaveCount(12, "an absurd setting is clamped to 12, not obeyed");
    }

    [Fact]
    public async Task RequestConcierge_AutoFanout_SpendsEachSlotOnADifferentCompany()
    {
        // The directory is imported one row per branch (the import dedupes on
        // slug), so a mover with two depots is two supplier rows behind one
        // info@. Spending two of the six slots on that one inbox sends the same
        // business two cold letters carrying two different quote tokens — it can
        // answer one request with two prices — while a competitor is never asked.
        //
        // Candidate ranking falls through exact-city and distance to supplier
        // name, so the two branches are the two the fan-out reaches for first.
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();

        SeedMovingProvider(db, "Kiirkolimine Kesklinn", "info@kiirkolimine.ee");
        SeedMovingProvider(db, "Kiirkolimine Mustamäe", "INFO@Kiirkolimine.EE");
        SeedMovingProvider(db, "Tallinna Kolija", "info@tallinnakolija.ee");
        SetSetting(db, "conciergeAutoOutreachMax", "2");
        await db.SaveChangesAsync();

        await MakeSupport(db, queue).RequestConcierge(new ConciergeRequest(
            Email: "cust@x.ee", City: "Tallinn", Categories: ["moving"]));

        ProviderEmails(queue).Select(e => e.To).Should().BeEquivalentTo(
            ["info@kiirkolimine.ee", "info@tallinnakolija.ee"],
            "the branch's slot passes to the next distinct company instead of "
          + "shrinking the fan-out or landing in the same inbox twice");
        db.ProviderOutreaches.Should().HaveCount(2);
        db.ProviderOutreaches.Select(o => o.QuoteToken).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task RequestConcierge_AutoFanout_DisabledBySetting_EmailsNobody()
    {
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();

        SeedMovingProvider(db, "Tallinn Movers", "m@x.ee");
        SetSetting(db, "conciergeAutoOutreach", "false");
        await db.SaveChangesAsync();

        var result = await MakeSupport(db, queue).RequestConcierge(new ConciergeRequest(
            Email: "cust@x.ee", City: "Tallinn", Categories: ["moving"]));

        result.Should().BeOfType<OkObjectResult>();
        ProviderEmails(queue).Should().BeEmpty("the master switch is off");
        db.ProviderOutreaches.Should().BeEmpty();
        db.DemandLeads.Single().Status.Should().Be(DemandLeadStatus.New);
        queue.Emails.Single(e => e.To == "info@ruumly.eu").TextBody
            .Should().Contain("Auto-outreach: OFF",
                "ops must be told the lead needs hand-work");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("I need a hand moving next week")]
    [InlineData("concierge: any | Tallinn")]
    public async Task AutoFanOut_AnyLeadNamingNoService_NeverBlastsTheDirectory(string? query)
    {
        // Superseded RequestConcierge_CategoryAny_NeverFansOut, which asserted that
        // a "moving"+"warehouse" pick reaches nobody — the defect, written down as
        // a test. What must still hold is the case the skip was actually written
        // for: an Any lead we cannot describe. ProviderCandidateFinder matches
        // EVERY supplier for such a lead, so a search here is a blast.
        //
        // Driven through the service rather than the intake because the intake
        // always writes a machine summary; these are legacy rows, other lead
        // sources, and raw customer text that must never steer who gets emailed.
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();

        SeedMovingProvider(db, "Tallinn Movers", "m@x.ee");
        SeedWarehouseProvider(db, "Tallinn Storage", "s@x.ee");
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "cust@x.ee", City = "Tallinn",
            Category = DemandLeadCategory.Any, Query = query, Language = "et",
            Source = "concierge", Status = DemandLeadStatus.New, CreatedAt = DateTime.UtcNow,
        };
        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();

        var summary = await TestServices.Outreach(db, queue).AutoFanOutAsync(lead);

        summary.Emailed.Should().Be(0);
        summary.SkipReason.Should().Be("category_any");
        queue.Emails.Should().BeEmpty("a request we cannot state must not cost anyone a cold contact");
        db.ProviderOutreaches.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestConcierge_AutoFanout_SkipsProvidersWithoutEmail_AndCountsThem()
    {
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();

        SeedMovingProvider(db, "Reachable", "reach@x.ee");
        SeedMovingProvider(db, "No Address One", null);
        SeedMovingProvider(db, "No Address Two", "   ");
        await db.SaveChangesAsync();

        await MakeSupport(db, queue).RequestConcierge(new ConciergeRequest(
            Email: "cust@x.ee", City: "Tallinn", Categories: ["moving"]));

        ProviderEmails(queue).Should().ContainSingle().Which.To.Should().Be("reach@x.ee");
        db.ProviderOutreaches.Should().ContainSingle();
        queue.Emails.Single(e => e.To == "info@ruumly.eu").TextBody
            .Should().Contain("(2 skipped: no email)",
                "ops needs to know which providers we cannot reach at all");
    }

    private sealed class ThrowingOutreach : IConciergeOutreachService
    {
        public Task<OutreachSendResult> SendAsync(DemandLead lead, IReadOnlyList<Guid> supplierIds,
            bool resend, string actor, CancellationToken ct = default)
            => throw new InvalidOperationException("boom");

        public Task<AutoOutreachSummary> AutoFanOutAsync(DemandLead lead, CancellationToken ct = default)
            => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task RequestConcierge_FanoutThrows_LeadStillSaved_AndRequestSucceeds()
    {
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();

        var result = await MakeSupport(db, queue, new ThrowingOutreach())
            .RequestConcierge(new ConciergeRequest(
                Email: "cust@x.ee", City: "Tallinn", Categories: ["moving"]));

        result.Should().BeOfType<OkObjectResult>(
            "a broken fanout must never 500 a real customer or lose the lead");
        db.DemandLeads.Should().ContainSingle();
        queue.Emails.Single(e => e.To == "info@ruumly.eu").TextBody
            .Should().Contain("Auto-outreach: FAILED",
                "the alert must confess the failure instead of implying providers were contacted");
    }

    [Fact]
    public async Task RequestConcierge_AutoFanout_NeverLeaksCustomerIdentityToProviders()
    {
        var db    = TestDbContext.Create();
        var queue = new CapturingEmailQueue();

        SeedMovingProvider(db, "Tallinn Movers", "m@x.ee");
        await db.SaveChangesAsync();

        await MakeSupport(db, queue).RequestConcierge(new ConciergeRequest(
            Email: "mari.maasikas@example.com", City: "Tallinn", Name: "Mari Maasikas",
            Phone: "+372 5555 1234", Categories: ["moving"],
            Details: "2-room flat"));

        var providerEmail = ProviderEmails(queue).Should().ContainSingle().Subject;
        providerEmail.ReplyTo.Should().Be("info@ruumly.eu", "a provider's reply must land in the ops inbox");
        providerEmail.HtmlBody.Should().NotBeNullOrWhiteSpace("the outreach email ships HTML, not plain text only");
        foreach (var secret in new[] { "mari.maasikas@example.com", "Mari Maasikas", "+372 5555 1234" })
        {
            providerEmail.Subject.Should().NotContain(secret);
            providerEmail.TextBody.Should().NotContain(secret);
            providerEmail.HtmlBody.Should().NotContain(secret);
        }
    }

    // ─── Admin lifecycle: first-touch stamp ───────────────────────────────────

    [Fact]
    public async Task UpdateLead_StatusMoveOutOfNew_StampsContactedAt_Once()
    {
        var db   = TestDbContext.Create();
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "c@x.ee", City = "Tallinn",
            Category = DemandLeadCategory.Any, Status = DemandLeadStatus.New,
            Language = "et", CreatedAt = DateTime.UtcNow,
        };
        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();

        var admin = MakeAdmin(db);

        (await admin.UpdateLead(lead.Id, new UpdateLeadRequest("contacted", null)))
            .Should().BeOfType<OkObjectResult>();
        var stamped = db.DemandLeads.Single().ContactedAt;
        stamped.Should().NotBeNull("moving out of New is the first admin touch");

        (await admin.UpdateLead(lead.Id, new UpdateLeadRequest("quoted", null)))
            .Should().BeOfType<OkObjectResult>();
        db.DemandLeads.Single().ContactedAt.Should().Be(stamped,
            "the first-touch timestamp is stamped once and never overwritten");
    }

    // ─── Admin request-field corrections ──────────────────────────────────────

    private static async Task<DemandLead> SeedConciergeLead(RuumlyDbContext db)
    {
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "old@x.ee", City = "Tallinn",
            Category = DemandLeadCategory.Any, Status = DemandLeadStatus.New,
            Language = "et", Source = "concierge", CreatedAt = DateTime.UtcNow,
            Name = "Old Name", Phone = "+372 1", ToCity = "Tartu",
            Details = "old details",
        };
        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();
        return lead;
    }

    [Fact]
    public async Task UpdateLead_EditsEveryRequestField_RoundTrips()
    {
        var db   = TestDbContext.Create();
        var lead = await SeedConciergeLead(db);

        var result = await MakeAdmin(db).UpdateLead(lead.Id, new UpdateLeadRequest(
            Name: "New Name", Email: "new@x.ee", Phone: "+372 9999",
            Category: "moving", City: "Pärnu", ToCity: "Narva",
            NeedDate: "2026-09-01",
            Details: "3-room flat, 2nd floor, no lift"));

        result.Should().BeOfType<OkObjectResult>();

        var saved = db.DemandLeads.Single();
        saved.Name.Should().Be("New Name");
        saved.Email.Should().Be("new@x.ee");
        saved.Phone.Should().Be("+372 9999");
        saved.Category.Should().Be(DemandLeadCategory.Moving);
        saved.City.Should().Be("Pärnu");
        saved.ToCity.Should().Be("Narva");
        saved.Details.Should().Be("3-room flat, 2nd floor, no lift");
        saved.NeedDate.Should().NotBeNull();
        saved.NeedDate!.Value.Should().Be(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task UpdateLead_NeedDate_NormalizedToUtc()
    {
        var db   = TestDbContext.Create();
        var lead = await SeedConciergeLead(db);

        await MakeAdmin(db).UpdateLead(lead.Id, new UpdateLeadRequest(
            // A bare "yyyy-MM-dd" — must be parsed and normalized to UTC or Npgsql
            // rejects the write to the timestamptz column.
            NeedDate: "2026-10-20"));

        var saved = db.DemandLeads.Single();
        saved.NeedDate!.Value.Kind.Should().Be(DateTimeKind.Utc);
        saved.NeedDate!.Value.Should().Be(new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc));
    }

    // Exercises the request body through REAL JSON binding (JsonSerializerDefaults.Web,
    // the same options ASP.NET Core uses) rather than in-process record construction —
    // the gap that let the original DateTime? NeedDate ship: "" cannot bind to
    // DateTime? and 400'd the whole edit before the handler ran. As a string it binds,
    // and "" now clears with the same empty-clears convention as every other field.
    private static UpdateLeadRequest FromJson(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<UpdateLeadRequest>(
            json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

    [Fact]
    public async Task UpdateLead_NeedDate_JsonBinding_Sets_LeavesUnchanged_Clears_AndRejects()
    {
        var db   = TestDbContext.Create();
        var lead = await SeedConciergeLead(db); // NeedDate starts null
        var admin = MakeAdmin(db);

        // {"needDate":"2026-08-01"} → set, UTC-normalized.
        (await admin.UpdateLead(lead.Id, FromJson("{\"needDate\":\"2026-08-01\"}")))
            .Should().BeOfType<OkObjectResult>();
        lead.NeedDate.Should().Be(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        lead.NeedDate!.Value.Kind.Should().Be(DateTimeKind.Utc);

        // Omitted (a different field patched) → needDate left unchanged.
        (await admin.UpdateLead(lead.Id, FromJson("{\"name\":\"Kept Date\"}")))
            .Should().BeOfType<OkObjectResult>();
        lead.NeedDate.Should().Be(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            "an omitted needDate is a no-op");

        // {"needDate":""} → CLEAR. This is the exact body the frontend sends on clear;
        // with the old DateTime? it 400'd the whole edit before the handler.
        (await admin.UpdateLead(lead.Id, FromJson("{\"needDate\":\"\"}")))
            .Should().BeOfType<OkObjectResult>();
        lead.NeedDate.Should().BeNull("an explicit empty string clears the date");

        // {"needDate":"not-a-date"} → 400, and no half-applied co-edit.
        (await admin.UpdateLead(lead.Id, FromJson("{\"needDate\":\"not-a-date\",\"city\":\"Narva\"}")))
            .Should().BeOfType<BadRequestObjectResult>();
        lead.NeedDate.Should().BeNull();
        lead.City.Should().Be("Tallinn", "a malformed date rejects the whole edit — nothing half-applies");
    }

    [Fact]
    public async Task UpdateLead_CategoryAny_IsAccepted()
    {
        var db   = TestDbContext.Create();
        var lead = await SeedConciergeLead(db);
        lead.Category = DemandLeadCategory.Moving;
        await db.SaveChangesAsync();

        (await MakeAdmin(db).UpdateLead(lead.Id, new UpdateLeadRequest(Category: "any")))
            .Should().BeOfType<OkObjectResult>();
        db.DemandLeads.Single().Category.Should().Be(DemandLeadCategory.Any);
    }

    [Fact]
    public async Task UpdateLead_InvalidEmail_Rejected_LeavesLeadUnchanged()
    {
        var db   = TestDbContext.Create();
        var lead = await SeedConciergeLead(db);

        (await MakeAdmin(db).UpdateLead(lead.Id, new UpdateLeadRequest(Email: "not-an-email")))
            .Should().BeOfType<BadRequestObjectResult>();
        db.DemandLeads.Single().Email.Should().Be("old@x.ee", "a rejected edit must not mutate the lead");
    }

    [Fact]
    public async Task UpdateLead_UnknownCategory_Rejected_LeavesLeadUnchanged()
    {
        var db   = TestDbContext.Create();
        var lead = await SeedConciergeLead(db);

        (await MakeAdmin(db).UpdateLead(lead.Id, new UpdateLeadRequest(Category: "teleportation")))
            .Should().BeOfType<BadRequestObjectResult>();
        db.DemandLeads.Single().Category.Should().Be(DemandLeadCategory.Any);
    }

    [Fact]
    public async Task UpdateLead_AngleBracketsInTextFields_Rejected()
    {
        var db = TestDbContext.Create();

        foreach (var body in new[]
        {
            new UpdateLeadRequest(Name:    "<script>alert(1)</script>"),
            new UpdateLeadRequest(City:    "Tallinn<b>"),
            new UpdateLeadRequest(ToCity:  "Tartu>"),
            new UpdateLeadRequest(Details: "a < b"),
        })
        {
            var lead = await SeedConciergeLead(db);
            (await MakeAdmin(db).UpdateLead(lead.Id, body))
                .Should().BeOfType<BadRequestObjectResult>();
        }
    }

    [Fact]
    public async Task UpdateLead_EmptyCity_Rejected()
    {
        var db   = TestDbContext.Create();
        var lead = await SeedConciergeLead(db);

        (await MakeAdmin(db).UpdateLead(lead.Id, new UpdateLeadRequest(City: "   ")))
            .Should().BeOfType<BadRequestObjectResult>();
        db.DemandLeads.Single().City.Should().Be("Tallinn");
    }

    [Fact]
    public async Task UpdateLead_PartialEdit_LeavesOmittedFieldsAndStatusUntouched()
    {
        var db   = TestDbContext.Create();
        var lead = await SeedConciergeLead(db);

        // Edit only the phone — nothing else, including status/ContactedAt, moves.
        (await MakeAdmin(db).UpdateLead(lead.Id, new UpdateLeadRequest(Phone: "+372 5550000")))
            .Should().BeOfType<OkObjectResult>();

        var saved = db.DemandLeads.Single();
        saved.Phone.Should().Be("+372 5550000");
        saved.Name.Should().Be("Old Name");
        saved.Email.Should().Be("old@x.ee");
        saved.City.Should().Be("Tallinn");
        saved.ToCity.Should().Be("Tartu");
        saved.Details.Should().Be("old details");
        saved.Status.Should().Be(DemandLeadStatus.New, "a request-field edit must not change status");
        saved.ContactedAt.Should().BeNull("a request-field edit is not a first admin touch");
    }

    [Fact]
    public async Task UpdateLead_RequestFieldsAndStatus_CanChangeTogether()
    {
        var db   = TestDbContext.Create();
        var lead = await SeedConciergeLead(db);

        (await MakeAdmin(db).UpdateLead(lead.Id, new UpdateLeadRequest(
            Status: "contacted", AdminNotes: "called them", City: "Rakvere")))
            .Should().BeOfType<OkObjectResult>();

        var saved = db.DemandLeads.Single();
        saved.City.Should().Be("Rakvere");
        saved.Status.Should().Be(DemandLeadStatus.Contacted);
        saved.AdminNotes.Should().Be("called them");
        saved.ContactedAt.Should().NotBeNull("moving out of New still stamps first touch");
    }

    // ─── Match suggestions ────────────────────────────────────────────────────

    [Fact]
    public async Task GetLeadMatches_FiltersByCategory_SameCityFirst_ExcludesInactive()
    {
        var db = TestDbContext.Create();

        Supplier MakeSupplier(string name, bool active = true)
        {
            var s = new Supplier
            {
                Id = Guid.NewGuid(), Name = name, ContactName = "C",
                ContactEmail = $"{name}@x.ee".ToLower(), ContactPhone = "1", IsActive = active,
            };
            db.Suppliers.Add(s);
            return s;
        }
        Listing MakeListing(Supplier s, ListingType type, string city, DateTime updatedAt, bool active = true)
        {
            var l = new Listing
            {
                Id = Guid.NewGuid(), SupplierId = s.Id, Supplier = s, Type = type,
                Title = $"{type} in {city}", City = city, IsActive = active,
                PriceFrom = 50m, PriceUnit = "onetime", UpdatedAt = updatedAt,
            };
            db.Listings.Add(l);
            return l;
        }

        var now      = DateTime.UtcNow;
        var tallinnS = MakeSupplier("TallinnMover");
        var tartuS   = MakeSupplier("TartuMover");
        var deadS    = MakeSupplier("DeadMover", active: false);

        // Tartu listing is fresher, but the Tallinn one must rank first (city match).
        var tallinnL = MakeListing(tallinnS, ListingType.Moving, "tallinn", now.AddDays(-5));
        var tartuL   = MakeListing(tartuS,   ListingType.Moving, "Tartu",   now.AddDays(-1));
        MakeListing(tartuS, ListingType.Warehouse, "Tallinn", now);                    // wrong category
        MakeListing(tallinnS, ListingType.Moving, "Tallinn", now, active: false);      // inactive listing
        MakeListing(deadS, ListingType.Moving, "Tallinn", now);                        // inactive supplier

        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "c@x.ee", City = "Tallinn",
            Category = DemandLeadCategory.Moving, Status = DemandLeadStatus.New,
            Language = "et", CreatedAt = now,
        };
        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();

        var result = await MakeAdmin(db).GetLeadMatches(lead.Id);
        var ok     = result.Should().BeOfType<OkObjectResult>().Subject;
        var items  = ((System.Collections.IEnumerable)ok.Value!).Cast<object>().ToList();

        items.Should().HaveCount(2, "wrong-category, inactive-listing and inactive-supplier rows are excluded");
        Prop(items[0], "listingId").Should().Be(tallinnL.Id, "same-city (case-insensitive) matches rank first");
        Prop(items[0], "supplierName").Should().Be("TallinnMover");
        Prop(items[0], "contactEmail").Should().Be("tallinnmover@x.ee");
        Prop(items[1], "listingId").Should().Be(tartuL.Id);
    }

    // ─── Queue filters + needs-response (SLA) view ────────────────────────────

    private static (int Total, List<object> Items) ReadLeads(IActionResult result)
    {
        var v     = result.Should().BeOfType<OkObjectResult>().Subject.Value!;
        var total = (int)v.GetType().GetProperty("total")!.GetValue(v)!;
        var items = ((System.Collections.IEnumerable)v.GetType().GetProperty("items")!.GetValue(v)!)
            .Cast<object>().ToList();
        return (total, items);
    }

    [Fact]
    public async Task GetLeads_FiltersBySourceCategoryCity_AndNeedsResponseOrdersOldestUncontactedFirst()
    {
        var db  = TestDbContext.Create();
        var now = DateTime.UtcNow;

        DemandLead Lead(string source, DemandLeadCategory cat, string city,
            DemandLeadStatus status, DateTime created, DateTime? contactedAt = null) => new()
        {
            Id = Guid.NewGuid(), Email = "c@x.ee", City = city, Category = cat,
            Language = "et", Source = source, Status = status,
            CreatedAt = created, ContactedAt = contactedAt,
        };

        var oldNew    = Lead("concierge", DemandLeadCategory.Moving,   "Tallinn", DemandLeadStatus.New,       now.AddDays(-5));
        var newNew    = Lead("concierge", DemandLeadCategory.Cleaning, "Tartu",   DemandLeadStatus.New,       now.AddDays(-1));
        var contacted = Lead("concierge", DemandLeadCategory.Moving,   "Tallinn", DemandLeadStatus.Contacted, now.AddDays(-3), now.AddDays(-3).AddMinutes(30));
        var routed    = Lead("routed",    DemandLeadCategory.Moving,   "Tallinn", DemandLeadStatus.New,       now.AddDays(-2));
        db.DemandLeads.AddRange(oldNew, newNew, contacted, routed);
        await db.SaveChangesAsync();

        var admin = MakeAdmin(db);

        // source filter (case-insensitive).
        ReadLeads(await admin.GetLeads(status: null, source: "CONCIERGE", category: null, city: null))
            .Total.Should().Be(3, "the routed lead is a different channel");
        ReadLeads(await admin.GetLeads(status: null, source: "routed", category: null, city: null))
            .Total.Should().Be(1);

        // category filter (slug, case-insensitive).
        ReadLeads(await admin.GetLeads(status: null, source: null, category: "Cleaning", city: null))
            .Total.Should().Be(1, "only the Tartu cleaning lead matches");

        // city filter (case-insensitive).
        ReadLeads(await admin.GetLeads(status: null, source: null, category: null, city: "tartu"))
            .Total.Should().Be(1);

        // needsResponse: only New + ContactedAt==null, oldest-first, across all sources.
        var (needsTotal, needsItems) = ReadLeads(
            await admin.GetLeads(status: null, source: null, category: null, city: null, needsResponse: true));
        needsTotal.Should().Be(3, "the Contacted lead is excluded from the SLA view");
        Prop(needsItems[0], "Id").Should().Be(oldNew.Id, "the oldest un-worked request surfaces first");
        Prop(needsItems[^1], "Id").Should().Be(newNew.Id, "the newest un-worked request is last");
    }

    // ─── Ops metrics ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLeadMetrics_ComputesFunnelOverLast30Days()
    {
        var db  = TestDbContext.Create();
        var now = DateTime.UtcNow;

        DemandLead Lead(DateTime createdAt, DemandLeadStatus status,
            DateTime? contactedAt = null, decimal? quotedPrice = null) => new()
        {
            Id = Guid.NewGuid(), Email = "c@x.ee", City = "Tallinn",
            Category = DemandLeadCategory.Any, Language = "et",
            // Source == "concierge" — the north-star funnel only counts the demand
            // channel; other sources are excluded (see the isolation test below).
            Source = "concierge",
            CreatedAt = createdAt, Status = status,
            ContactedAt = contactedAt, QuotedPrice = quotedPrice,
        };

        db.DemandLeads.AddRange(
            // In the 7d window (and 30d):
            Lead(now.AddDays(-1), DemandLeadStatus.New),                                              // untouched
            Lead(now.AddDays(-2), DemandLeadStatus.Contacted, now.AddDays(-2).AddMinutes(30)),        // 30 min response
            Lead(now.AddDays(-3), DemandLeadStatus.Quoted,    now.AddDays(-3).AddMinutes(90), 100m),  // 90 min response
            Lead(now.AddDays(-5), DemandLeadStatus.Converted, now.AddDays(-5).AddMinutes(10)),        // 10 min response
            // In 30d but outside 7d:
            Lead(now.AddDays(-10), DemandLeadStatus.New),
            // Outside the 30d window entirely — must not count anywhere:
            Lead(now.AddDays(-40), DemandLeadStatus.Converted, now.AddDays(-40).AddMinutes(5)));
        await db.SaveChangesAsync();

        var result = await MakeAdmin(db).GetLeadMetrics();
        var body   = result.Should().BeOfType<OkObjectResult>().Subject.Value!;

        Prop(body, "requestsThisWeek").Should().Be(4);
        Prop(body, "requests30d").Should().Be(5);
        // Contacted: the 3 touched leads out of 5 in-window.
        Prop(body, "contactRate30d").Should().Be(0.6);
        // Quoted-or-beyond: Quoted (has price) + Converted = 2 of 5.
        Prop(body, "quoteRate30d").Should().Be(0.4);
        // Booked: 1 Converted / 2 quoted-or-beyond.
        Prop(body, "bookingRate30d").Should().Be(0.5);
        // Median of [10, 30, 90] minutes.
        Prop(body, "medianFirstResponseMinutes").Should().Be(30);

        // Match rate: base = leads that left New (Contacted, Quoted, Converted = 3).
        // Matched = Quoted + Converted (2); the Contacted lead has no offer/replied
        // outreach so it's not yet a match.
        var matchRate = Prop(body, "matchRate30d")!;
        Prop(matchRate, "matched").Should().Be(2);
        Prop(matchRate, "total").Should().Be(3);
        ((double)Prop(matchRate, "rate")!).Should().BeApproximately(2.0 / 3.0, 1e-9);
    }

    [Fact]
    public async Task GetLeadMetrics_NoTouchedLeads_MedianIsNull()
    {
        var db = TestDbContext.Create();
        db.DemandLeads.Add(new DemandLead
        {
            Id = Guid.NewGuid(), Email = "c@x.ee", City = "Tallinn",
            Category = DemandLeadCategory.Any, Language = "et", Source = "concierge",
            CreatedAt = DateTime.UtcNow, Status = DemandLeadStatus.New,
        });
        await db.SaveChangesAsync();

        var body = (await MakeAdmin(db).GetLeadMetrics())
            .Should().BeOfType<OkObjectResult>().Subject.Value!;

        Prop(body, "medianFirstResponseMinutes").Should().BeNull();
        Prop(body, "contactRate30d").Should().Be(0d);
    }

    // ─── Metrics honesty (concierge scoping + response/contact pollution) ──────

    [Fact]
    public async Task GetLeadMetrics_OnlyCountsConciergeSource_ExcludesRoutedAndOther()
    {
        var db  = TestDbContext.Create();
        var now = DateTime.UtcNow;

        DemandLead Lead(string? source, DemandLeadStatus status) => new()
        {
            Id = Guid.NewGuid(), Email = "c@x.ee", City = "Tallinn",
            Category = DemandLeadCategory.Any, Language = "et", Source = source,
            CreatedAt = now.AddDays(-1), Status = status,
            ContactedAt = status != DemandLeadStatus.New ? now.AddDays(-1).AddMinutes(20) : null,
        };

        db.DemandLeads.AddRange(
            Lead("concierge", DemandLeadStatus.New),        // the only demand-funnel request
            Lead("routed",    DemandLeadStatus.Converted),  // partner-direct — must NOT count
            Lead("notify-interest", DemandLeadStatus.Quoted),// legacy capture — must NOT count
            Lead(null,        DemandLeadStatus.Converted));  // untagged legacy — must NOT count
        await db.SaveChangesAsync();

        var body = (await MakeAdmin(db).GetLeadMetrics())
            .Should().BeOfType<OkObjectResult>().Subject.Value!;

        Prop(body, "requestsThisWeek").Should().Be(1, "only the concierge lead is a demand-funnel request");
        Prop(body, "requests30d").Should().Be(1);
        // The routed/notify/null Converted+Quoted leads must not inflate any rate.
        Prop(body, "quoteRate30d").Should().Be(0d);
        Prop(body, "bookingRate30d").Should().Be(0d);
        var matchRate = Prop(body, "matchRate30d")!;
        Prop(matchRate, "total").Should().Be(0, "the lone concierge lead is still New — nothing has left New");
    }

    [Fact]
    public async Task GetLeadMetrics_DismissedAndUnmatched_DoNotCountAsContactOrResponse()
    {
        var db  = TestDbContext.Create();
        var now = DateTime.UtcNow;

        // Two concierge leads closed WITHOUT genuine contact. The lifecycle must
        // not stamp ContactedAt for these, and the metrics must not treat them as
        // contacted or sample them for response time (else dismissing spam makes
        // the ops team look instant and inflates the contact rate).
        var dismissed = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "spam@x.ee", City = "Tallinn",
            Category = DemandLeadCategory.Any, Language = "et", Source = "concierge",
            CreatedAt = now.AddDays(-1), Status = DemandLeadStatus.New,
        };
        var unmatched = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "nomatch@x.ee", City = "Tallinn",
            Category = DemandLeadCategory.Any, Language = "et", Source = "concierge",
            CreatedAt = now.AddDays(-2), Status = DemandLeadStatus.New,
        };
        db.DemandLeads.AddRange(dismissed, unmatched);
        await db.SaveChangesAsync();

        var admin = MakeAdmin(db);
        (await admin.UpdateLead(dismissed.Id, new UpdateLeadRequest("dismissed"))).Should().BeOfType<OkObjectResult>();
        (await admin.UpdateLead(unmatched.Id, new UpdateLeadRequest("unmatched"))).Should().BeOfType<OkObjectResult>();

        db.DemandLeads.Single(l => l.Id == dismissed.Id).ContactedAt.Should().BeNull(
            "dismissing a lead is not genuine customer contact");
        db.DemandLeads.Single(l => l.Id == unmatched.Id).ContactedAt.Should().BeNull(
            "an unmatched lead was never actually contacted");

        var body = (await admin.GetLeadMetrics()).Should().BeOfType<OkObjectResult>().Subject.Value!;
        Prop(body, "requests30d").Should().Be(2);
        Prop(body, "contactRate30d").Should().Be(0d, "no lead was genuinely contacted");
        Prop(body, "medianFirstResponseMinutes").Should().BeNull(
            "closures must never enter the first-response sample");
    }

    [Fact]
    public async Task GetLeadMetrics_ContactedThenQuotedThenLost_StillCountsAsContact_NoFunnelInversion()
    {
        var db  = TestDbContext.Create();
        var now = DateTime.UtcNow;

        // The NORMAL end state of a worked-but-didn't-book request:
        // Received → Contacted → Quoted → Lost (Dismissed). It was genuinely
        // contacted (and quoted), then closed — so it MUST still count in
        // contactRate30d and the response-time sample, otherwise contactRate can
        // drop below quoteRate (a logically impossible funnel inversion), because
        // quotedOrBeyond survives the closure via QuotedPrice.
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "lost@x.ee", City = "Tallinn",
            Category = DemandLeadCategory.Any, Language = "et", Source = "concierge",
            CreatedAt = now.AddDays(-2), Status = DemandLeadStatus.New,
        };
        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();

        var admin = MakeAdmin(db);
        // Contacted → stamps ContactedAt (genuine first touch).
        (await admin.UpdateLead(lead.Id, new UpdateLeadRequest("contacted"))).Should().BeOfType<OkObjectResult>();
        lead.ContactedAt.Should().NotBeNull();
        // A quote was sent (QuotedPrice survives closure), then the lead is Lost.
        lead.QuotedPrice = 250m;
        await db.SaveChangesAsync();
        (await admin.UpdateLead(lead.Id, new UpdateLeadRequest("dismissed"))).Should().BeOfType<OkObjectResult>();

        var stored = db.DemandLeads.Single();
        stored.Status.Should().Be(DemandLeadStatus.Dismissed, "the lead ended Lost/closed");
        stored.ContactedAt.Should().NotBeNull("closure never clears a real first-touch");

        var body = (await admin.GetLeadMetrics()).Should().BeOfType<OkObjectResult>().Subject.Value!;
        var contactRate = (double)Prop(body, "contactRate30d")!;
        var quoteRate   = (double)Prop(body, "quoteRate30d")!;

        contactRate.Should().Be(1d, "a contacted-then-lost lead is still a genuine contact");
        quoteRate.Should().Be(1d, "QuotedPrice survives closure");
        contactRate.Should().BeGreaterThanOrEqualTo(quoteRate,
            "contactRate can never be below quoteRate — the funnel must not invert");
        Prop(body, "medianFirstResponseMinutes").Should().NotBeNull(
            "the real first contact must still enter the response sample");
    }

    [Fact]
    public async Task GetLeadMetrics_MatchRate_CountsOfferAndRepliedOutreachSignals()
    {
        var db  = TestDbContext.Create();
        var now = DateTime.UtcNow;

        DemandLead Lead(DemandLeadStatus status) => new()
        {
            Id = Guid.NewGuid(), Email = "c@x.ee", City = "Tallinn",
            Category = DemandLeadCategory.Any, Language = "et", Source = "concierge",
            CreatedAt = now.AddDays(-3), Status = status,
            ContactedAt = now.AddDays(-3).AddMinutes(15),
        };

        // Contacted lead WITH a live offer → matched via the offer signal.
        var withOffer   = Lead(DemandLeadStatus.Contacted);
        // Contacted lead WITH a replied outreach → matched via the outreach signal.
        var withReply   = Lead(DemandLeadStatus.Contacted);
        // Contacted lead with neither → a genuine miss (worked, not matched).
        var withNothing = Lead(DemandLeadStatus.Contacted);
        // Explicit miss.
        var unmatched   = Lead(DemandLeadStatus.Unmatched);
        db.DemandLeads.AddRange(withOffer, withReply, withNothing, unmatched);

        db.Offers.Add(new Offer
        {
            Id = Guid.NewGuid(), DemandLeadId = withOffer.Id, Token = "tok-1",
            Status = OfferStatus.Sent, Language = "et",
        });
        db.ProviderOutreaches.Add(new ProviderOutreach
        {
            Id = Guid.NewGuid(), DemandLeadId = withReply.Id, SupplierId = Guid.NewGuid(),
            SentTo = "p@x.ee", Status = ProviderOutreachStatus.Replied,
        });
        await db.SaveChangesAsync();

        var body = (await MakeAdmin(db).GetLeadMetrics())
            .Should().BeOfType<OkObjectResult>().Subject.Value!;

        var matchRate = Prop(body, "matchRate30d")!;
        Prop(matchRate, "total").Should().Be(4, "all four leads left New");
        Prop(matchRate, "matched").Should().Be(2, "offer + replied-outreach signals count; the bare Contacted and Unmatched do not");
        ((double)Prop(matchRate, "rate")!).Should().Be(0.5);
    }
}
