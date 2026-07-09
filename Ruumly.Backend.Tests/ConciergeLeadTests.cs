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
        public List<(string To, string Subject, string TextBody)> Emails { get; } = [];
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null)
            => Emails.Add((to, subject, textBody));
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

    private static SupportController MakeSupport(RuumlyDbContext db, IBackgroundEmailQueue queue) =>
        new(db, queue, new NoOpNotifications())
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
            NeedDate: new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc),
            Details: "2-room flat plus some pallets", Language: "en"));

        result.Should().BeOfType<OkObjectResult>();

        var lead = db.DemandLeads.Single();
        lead.Category.Should().Be(DemandLeadCategory.Any, "more than one valid category falls back to Any");
        lead.Source.Should().Be("concierge");
        lead.Status.Should().Be(DemandLeadStatus.New);
        lead.City.Should().Be("Tallinn");
        lead.ToCity.Should().Be("Tartu");
        lead.NeedDate.Should().NotBeNull();
        lead.Details.Should().Be("2-room flat plus some pallets");
        lead.Language.Should().Be("en");

        // Compact ENGLISH machine summary — categories + route, never translated labels.
        lead.Query.Should().StartWith("concierge:");
        lead.Query.Should().Contain("moving");
        lead.Query.Should().Contain("warehouse");
        lead.Query.Should().Contain("Tallinn");
        lead.Query.Should().Contain("2026-08-15");

        queue.Emails.Should().ContainSingle(e => e.To == "admin@ruumly.eu",
            "an unrouted concierge lead is worked by the admin team");
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
    }

    [Fact]
    public async Task GetLeadMetrics_NoTouchedLeads_MedianIsNull()
    {
        var db = TestDbContext.Create();
        db.DemandLeads.Add(new DemandLead
        {
            Id = Guid.NewGuid(), Email = "c@x.ee", City = "Tallinn",
            Category = DemandLeadCategory.Any, Language = "et",
            CreatedAt = DateTime.UtcNow, Status = DemandLeadStatus.New,
        });
        await db.SaveChangesAsync();

        var body = (await MakeAdmin(db).GetLeadMetrics())
            .Should().BeOfType<OkObjectResult>().Subject.Value!;

        Prop(body, "medianFirstResponseMinutes").Should().BeNull();
        Prop(body, "contactRate30d").Should().Be(0d);
    }
}
