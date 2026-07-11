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
/// Wave K — moving/trailer "request a quote" lead lifecycle. A public quote
/// request becomes a DemandLead routed to the listing's partner; the partner
/// replies with a one-time price; the customer is emailed. Closes the previously
/// dead demand-lead loop (leads that reached no one and went nowhere).
/// </summary>
public class MovingQuoteLeadTests
{
    private sealed class CapturingEmailQueue : IBackgroundEmailQueue
    {
        public List<(string To, string Subject, string TextBody)> Emails { get; } = [];
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null)
            => Emails.Add((to, subject, textBody));
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

    private static (Supplier supplier, Listing listing, User provider) Seed(RuumlyDbContext db)
    {
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = "Mover OÜ", ContactName = "C",
            ContactEmail = "partner@mover.ee", ContactPhone = "1",
        };
        var listing = new Listing
        {
            Id = Guid.NewGuid(), SupplierId = supplier.Id, Type = ListingType.Moving,
            Title = "City move", City = "Tallinn", IsActive = true, PriceUnit = "onetime",
        };
        var provider = new User
        {
            Id = Guid.NewGuid(), Name = "P", Email = "p@mover.ee",
            Role = UserRole.Provider, SupplierId = supplier.Id,
        };
        db.Suppliers.Add(supplier);
        db.Listings.Add(listing);
        db.Users.Add(provider);
        db.SaveChanges();
        return (supplier, listing, provider);
    }

    private static SupportController MakeSupport(
        RuumlyDbContext db, IBackgroundEmailQueue queue, INotificationService notif) =>
        new(db, queue, notif)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static ProviderLeadsController MakeProvider(
        RuumlyDbContext db, IBackgroundEmailQueue queue, Guid userId, UserRole role)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, role.ToString()),
            ], "test")),
        };
        return new ProviderLeadsController(db, queue)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    // ─── Public quote request → routed lead ──────────────────────────────────

    [Fact]
    public async Task RequestQuote_CreatesRoutedLead_AndNotifiesPartner()
    {
        var db = TestDbContext.Create();
        var (supplier, listing, provider) = Seed(db);
        var queue = new CapturingEmailQueue();
        var notif = new CapturingNotifications();

        var result = await MakeSupport(db, queue, notif).RequestQuote(new QuoteLeadRequest(
            ListingId: listing.Id, Email: "cust@x.ee", Name: "Cust", Phone: "+372 5",
            City: null, Message: "2 rooms, 3rd floor", Language: "et"));

        result.Should().BeOfType<OkObjectResult>();

        var lead = db.DemandLeads.Single();
        lead.SupplierId.Should().Be(supplier.Id, "the lead is routed to the listing's partner");
        lead.ListingId.Should().Be(listing.Id);
        lead.Category.Should().Be(DemandLeadCategory.Moving);
        lead.Status.Should().Be(DemandLeadStatus.New);
        lead.Email.Should().Be("cust@x.ee");
        lead.Source.Should().Be("routed", "a routed quote is tagged so it never pollutes the concierge funnel metrics");

        // The owning provider gets an in-app notification, partner + ops get email.
        notif.Created.Should().ContainSingle(n => n.UserId == provider.Id);
        queue.Emails.Should().Contain(e => e.To == supplier.ContactEmail);
        queue.Emails.Should().Contain(e => e.To == "info@ruumly.eu",
            "routed-quote alerts go to the unified ops inbox (opsInbox, default info@)");
    }

    [Fact]
    public async Task RequestQuote_UnknownListing_404_NoLead()
    {
        var db = TestDbContext.Create();
        Seed(db);

        var result = await MakeSupport(db, new CapturingEmailQueue(), new CapturingNotifications())
            .RequestQuote(new QuoteLeadRequest(ListingId: Guid.NewGuid(), Email: "c@x.ee"));

        result.Should().BeOfType<NotFoundObjectResult>();
        db.DemandLeads.Should().BeEmpty();
    }

    // ─── Partner reply with a quote ──────────────────────────────────────────

    [Fact]
    public async Task Respond_OwningProvider_SetsQuote_AndEmailsCustomer()
    {
        var db = TestDbContext.Create();
        var (supplier, listing, provider) = Seed(db);
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "cust@x.ee", Name = "Cust", City = "Tallinn",
            Category = DemandLeadCategory.Moving, SupplierId = supplier.Id, ListingId = listing.Id,
            Status = DemandLeadStatus.New, Language = "en", CreatedAt = DateTime.UtcNow,
        };
        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();

        var queue = new CapturingEmailQueue();
        var result = await MakeProvider(db, queue, provider.Id, UserRole.Provider)
            .Respond(lead.Id, new RespondLeadRequest(Status: null, QuotedPrice: 240m, ProviderNotes: "incl. packing"));

        result.Should().BeOfType<OkObjectResult>();
        var fresh = db.DemandLeads.Single();
        fresh.QuotedPrice.Should().Be(240m);
        fresh.QuotedAt.Should().NotBeNull();
        fresh.RespondedAt.Should().NotBeNull();
        fresh.Status.Should().Be(DemandLeadStatus.Quoted, "a price quote advances a New lead to Quoted");
        fresh.ProviderNotes.Should().Be("incl. packing");
        queue.Emails.Should().ContainSingle(e => e.To == "cust@x.ee", "the customer is emailed the quote");
    }

    [Fact]
    public async Task Respond_ForeignProvider_403_NoChange()
    {
        var db = TestDbContext.Create();
        var (supplier, listing, _) = Seed(db);
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "cust@x.ee", City = "Tallinn",
            Category = DemandLeadCategory.Moving, SupplierId = supplier.Id, ListingId = listing.Id,
            Status = DemandLeadStatus.New, Language = "et", CreatedAt = DateTime.UtcNow,
        };
        db.DemandLeads.Add(lead);
        // A provider belonging to a DIFFERENT supplier.
        var other = new User
        {
            Id = Guid.NewGuid(), Name = "O", Email = "o@other.ee",
            Role = UserRole.Provider, SupplierId = Guid.NewGuid(),
        };
        db.Users.Add(other);
        await db.SaveChangesAsync();

        var result = await MakeProvider(db, new CapturingEmailQueue(), other.Id, UserRole.Provider)
            .Respond(lead.Id, new RespondLeadRequest(Status: null, QuotedPrice: 999m, ProviderNotes: null));

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        db.DemandLeads.Single().QuotedPrice.Should().BeNull("a foreign provider must not alter the lead");
    }

    // ─── Provider inbox is supplier-scoped ───────────────────────────────────

    [Fact]
    public async Task GetLeads_Provider_SeesOnlyOwnSupplierLeads()
    {
        var db = TestDbContext.Create();
        var (supplier, listing, provider) = Seed(db);
        db.DemandLeads.AddRange(
            new DemandLead { Id = Guid.NewGuid(), Email = "mine@x.ee", City = "Tallinn",
                Category = DemandLeadCategory.Moving, SupplierId = supplier.Id, ListingId = listing.Id,
                Status = DemandLeadStatus.New, Language = "et", CreatedAt = DateTime.UtcNow },
            new DemandLead { Id = Guid.NewGuid(), Email = "theirs@x.ee", City = "Tartu",
                Category = DemandLeadCategory.Moving, SupplierId = Guid.NewGuid(),
                Status = DemandLeadStatus.New, Language = "et", CreatedAt = DateTime.UtcNow },
            // A generic, unrouted demand-capture lead must never leak to a partner.
            new DemandLead { Id = Guid.NewGuid(), Email = "generic@x.ee", City = "Pärnu",
                Category = DemandLeadCategory.Any, SupplierId = null,
                Status = DemandLeadStatus.New, Language = "et", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var result = await MakeProvider(db, new CapturingEmailQueue(), provider.Id, UserRole.Provider)
            .GetLeads(supplierId: null, status: null);

        var ok    = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = (System.Collections.IEnumerable)ok.Value!.GetType().GetProperty("items")!.GetValue(ok.Value!)!;
        items.Cast<object>().Should().HaveCount(1, "only the caller's own supplier's leads are visible");
    }
}
