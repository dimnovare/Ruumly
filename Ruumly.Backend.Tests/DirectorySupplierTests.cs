using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
/// Provider DIRECTORY — unclaimed free profiles on the map. Admin bulk-imports
/// suppliers (POST /api/admin/suppliers/bulk) that get a pin + partner page but
/// no commerce; the concierge intake accepts the new event categories; lead
/// match suggestions union directory suppliers with listing-based matches; and
/// the public locations feed exposes the directory flags.
/// </summary>
public class DirectorySupplierTests
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

    private static AdminDirectoryController MakeDirectory(RuumlyDbContext db) =>
        new(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.Email, "admin@ruumly.eu"),
                        new Claim(ClaimTypes.Role, "Admin"),
                    ], "test")),
                },
            },
        };

    private static AdminLeadsController MakeAdminLeads(RuumlyDbContext db) =>
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

    private static LocationsController MakePublicLocations(RuumlyDbContext db) =>
        new(db)
        {
            // No identity — the anonymous/public map view.
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static SupportController MakeSupport(RuumlyDbContext db) =>
        new(db, new CapturingEmailQueue(), new NoOpNotifications(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static object? Prop(object o, string name) =>
        o.GetType().GetProperty(name)!.GetValue(o);

    private static DirectorySupplierRow Row(
        string name = "Puhastus OÜ", string slug = "puhastus-ou", string city = "Tallinn",
        double? lat = 59.437, double? lng = 24.754, List<string>? serviceTypes = null) =>
        new(
            Name: name, Slug: slug, City: city, Lat: lat, Lng: lng,
            ServiceTypes: serviceTypes ?? ["cleaning"],
            WebsiteUrl: "https://puhastus.ee", ContactEmail: "info@puhastus.ee",
            ContactPhone: "+372 555", Tagline: "Kolimisjärgne puhastus",
            DescriptionEt: "Puhastame kõik.", LogoUrl: "https://cdn/logo.png",
            RegistryCode: null);

    // ─── POST /api/admin/suppliers/bulk ───────────────────────────────────────

    [Fact]
    public async Task Bulk_CreatesPublishedDirectorySupplier_WithOneGeolocatedLocation()
    {
        var db = TestDbContext.Create();

        var result = await MakeDirectory(db).BulkCreateDirectorySuppliers([Row()]);
        var body   = result.Should().BeOfType<OkObjectResult>().Subject.Value!;

        ((List<string>)Prop(body, "created")!).Should().Equal("puhastus-ou");
        ((List<string>)Prop(body, "skipped")!).Should().BeEmpty();
        ((List<object>)Prop(body, "errors")!).Should().BeEmpty();

        var supplier = db.Suppliers.Single();
        supplier.Name.Should().Be("Puhastus OÜ");
        supplier.Slug.Should().Be("puhastus-ou");
        supplier.ContactName.Should().Be("Puhastus OÜ");
        supplier.ContactEmail.Should().Be("info@puhastus.ee");
        supplier.WebsiteUrl.Should().Be("https://puhastus.ee");
        supplier.IsActive.Should().BeTrue();
        supplier.IsPartnerPagePublished.Should().BeTrue("the partner page must render for directory profiles");
        supplier.IsDirectoryListing.Should().BeTrue();
        supplier.ServiceTypesJson.Should().Be("[\"cleaning\"]");
        supplier.Country.Should().Be("EE");
        supplier.IntegrationType.Should().Be(IntegrationType.Manual);
        supplier.LongDescriptionTranslationsJson.Should().Contain("\"et\"")
            .And.Contain("\"en\"").And.Contain("\"ru\"",
            "missing languages fall back to whichever description was provided");
        // No commerce for unclaimed profiles.
        supplier.BookingEnabled.Should().BeFalse();
        supplier.ContractSigningEnabled.Should().BeFalse();
        supplier.DirectPaymentEnabled.Should().BeFalse();
        supplier.RuumlyPaymentEnabled.Should().BeFalse();

        var location = db.SupplierLocations.Single();
        location.SupplierId.Should().Be(supplier.Id);
        location.Name.Should().Be("Puhastus OÜ");
        location.City.Should().Be("Tallinn");
        location.Address.Should().Be("Tallinn", "address falls back to city when not provided");
        location.Lat.Should().Be(59.437);
        location.Lng.Should().Be(24.754);
        location.IsActive.Should().BeTrue();
        location.IsSynthetic.Should().BeFalse("directory pins must show on the public map");
        db.Listings.Should().BeEmpty("directory locations carry zero units");

        db.AuditLogs.Should().ContainSingle(a => a.Action == "directory.bulk_import");
    }

    [Fact]
    public async Task Bulk_IsIdempotentBySlug_SecondCallSkips()
    {
        var db  = TestDbContext.Create();
        var ctl = MakeDirectory(db);

        await ctl.BulkCreateDirectorySuppliers([Row()]);
        var second = await ctl.BulkCreateDirectorySuppliers([Row(name: "Renamed OÜ")]);
        var body   = second.Should().BeOfType<OkObjectResult>().Subject.Value!;

        ((List<string>)Prop(body, "created")!).Should().BeEmpty();
        ((List<string>)Prop(body, "skipped")!).Should().Equal("puhastus-ou");
        db.Suppliers.Should().HaveCount(1, "an existing slug is skipped, never duplicated or modified");
        db.Suppliers.Single().Name.Should().Be("Puhastus OÜ");
        db.SupplierLocations.Should().HaveCount(1);
    }

    [Fact]
    public async Task Bulk_InvalidRows_ReportPerRowErrors_WithoutFailingTheBatch()
    {
        var db = TestDbContext.Create();

        var result = await MakeDirectory(db).BulkCreateDirectorySuppliers(
        [
            Row(name: "Bad Slug", slug: "Not A Slug!"),
            Row(name: "Bad Lat",  slug: "bad-lat", lat: 40.0),
            Row(name: "Bad Types", slug: "bad-types", serviceTypes: ["spaceship"]),
            Row(name: "No Types",  slug: "no-types", serviceTypes: []),
            Row(name: "Good One",  slug: "good-one"),
        ]);
        var body = result.Should().BeOfType<OkObjectResult>().Subject.Value!;

        // Row-level validation errors must never fail the batch.
        ((List<string>)Prop(body, "created")!).Should().Equal("good-one");
        var errors = (List<object>)Prop(body, "errors")!;
        errors.Should().HaveCount(4);
        Prop(errors[0], "slug").Should().Be("not a slug!");
        ((string)Prop(errors[0], "reason")!).Should().Contain("slug");
        ((string)Prop(errors[1], "reason")!).Should().Contain("lat");
        ((string)Prop(errors[2], "reason")!).Should().Contain("spaceship");
        ((string)Prop(errors[3], "reason")!).Should().Contain("serviceTypes");

        db.Suppliers.Should().HaveCount(1);
        db.Suppliers.Single().Slug.Should().Be("good-one");
    }

    [Fact]
    public async Task Bulk_EmptyOrOversizedBatch_ReturnsBadRequest()
    {
        var db  = TestDbContext.Create();
        var ctl = MakeDirectory(db);

        (await ctl.BulkCreateDirectorySuppliers([])).Should().BeOfType<BadRequestObjectResult>();

        var tooMany = Enumerable.Range(0, 501).Select(i => Row(slug: $"s-{i}")).ToList();
        (await ctl.BulkCreateDirectorySuppliers(tooMany)).Should().BeOfType<BadRequestObjectResult>();
        db.Suppliers.Should().BeEmpty();
    }

    // ─── Concierge intake: new event categories ───────────────────────────────

    [Fact]
    public async Task RequestConcierge_SingleCleaningCategory_MapsToCleaningEnum()
    {
        var db = TestDbContext.Create();

        var result = await MakeSupport(db).RequestConcierge(new ConciergeRequest(
            Email: "cust@x.ee", City: "Tallinn", Categories: ["Cleaning"]));

        result.Should().BeOfType<OkObjectResult>();
        var lead = db.DemandLeads.Single();
        lead.Category.Should().Be(DemandLeadCategory.Cleaning);
        lead.Query.Should().Contain("cleaning", "the machine summary lists the requested categories");
    }

    [Theory]
    [InlineData("packing",   DemandLeadCategory.Packing)]
    [InlineData("VANRENTAL", DemandLeadCategory.VanRental)]
    [InlineData("insurance", DemandLeadCategory.Insurance)]
    public async Task RequestConcierge_AcceptsNewCategorySlugs_CaseInsensitive(
        string slug, DemandLeadCategory expected)
    {
        var db = TestDbContext.Create();

        await MakeSupport(db).RequestConcierge(new ConciergeRequest(
            Email: "cust@x.ee", City: "Tallinn", Categories: [slug]));

        db.DemandLeads.Single().Category.Should().Be(expected);
    }

    // ─── GET /api/admin/leads/{id}/matches — directory union ────────────────

    [Fact]
    public async Task GetLeadMatches_ReturnsDirectorySuppliers_ForCleaningLead_SameCityFirst()
    {
        var db = TestDbContext.Create();

        Supplier Directory(string name, string city, string typesJson, bool active = true)
        {
            var s = new Supplier
            {
                Id = Guid.NewGuid(), Name = name, ContactName = name,
                ContactEmail = $"{name}@x.ee".ToLower(), ContactPhone = "1",
                IsActive = active, IsDirectoryListing = true, Slug = name.ToLower(),
                ServiceTypesJson = typesJson,
            };
            db.Suppliers.Add(s);
            db.SupplierLocations.Add(new SupplierLocation
            {
                Id = Guid.NewGuid(), SupplierId = s.Id, Name = name,
                Address = city, City = city, Lat = 59.4, Lng = 24.7, IsActive = true,
            });
            return s;
        }

        var tartu   = Directory("tartuclean",   "Tartu",   """["cleaning","packing"]""");
        var tallinn = Directory("tallinnclean", "tallinn", """["cleaning"]""");
        Directory("moverco", "Tallinn", """["moving"]""");                    // wrong category
        Directory("deadclean", "Tallinn", """["cleaning"]""", active: false); // inactive

        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "c@x.ee", City = "Tallinn",
            Category = DemandLeadCategory.Cleaning, Status = DemandLeadStatus.New,
            Language = "et", CreatedAt = DateTime.UtcNow,
        };
        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();

        var result = await MakeAdminLeads(db).GetLeadMatches(lead.Id);
        var ok     = result.Should().BeOfType<OkObjectResult>().Subject;
        var items  = ((System.Collections.IEnumerable)ok.Value!).Cast<object>().ToList();

        items.Should().HaveCount(2, "wrong-category and inactive directory suppliers are excluded");
        Prop(items[0], "supplierId").Should().Be(tallinn.Id, "same-city (case-insensitive) ranks first");
        Prop(items[0], "isDirectory").Should().Be(true);
        Prop(items[0], "listingId").Should().BeNull("directory rows have no listing");
        Prop(items[0], "listingTitle").Should().BeNull();
        Prop(items[0], "price").Should().BeNull();
        Prop(items[0], "priceUnit").Should().BeNull();
        Prop(items[0], "contactEmail").Should().Be("tallinnclean@x.ee");
        Prop(items[0], "listingCity").Should().Be("tallinn");
        ((IEnumerable<string>)Prop(items[0], "serviceTypes")!)
            .Should().Contain("cleaning", "the admin must see which services a directory supplier covers");
        Prop(items[1], "supplierId").Should().Be(tartu.Id);
    }

    [Fact]
    public async Task GetLeadMatches_ListingMatchesRankAboveDirectoryRows()
    {
        var db = TestDbContext.Create();

        var listingSupplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = "MoverListing", ContactName = "C",
            ContactEmail = "l@x.ee", ContactPhone = "1", IsActive = true,
        };
        db.Suppliers.Add(listingSupplier);
        var listing = new Listing
        {
            Id = Guid.NewGuid(), SupplierId = listingSupplier.Id, Supplier = listingSupplier,
            Type = ListingType.Moving, Title = "Moving in Tartu", City = "Tartu",
            IsActive = true, PriceFrom = 80m, PriceUnit = "onetime", UpdatedAt = DateTime.UtcNow,
        };
        db.Listings.Add(listing);

        var dirSupplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = "MoverDirectory", ContactName = "C",
            ContactEmail = "d@x.ee", ContactPhone = "2", IsActive = true,
            IsDirectoryListing = true, Slug = "moverdirectory",
            ServiceTypesJson = """["moving"]""",
        };
        db.Suppliers.Add(dirSupplier);
        db.SupplierLocations.Add(new SupplierLocation
        {
            Id = Guid.NewGuid(), SupplierId = dirSupplier.Id, Name = "MoverDirectory",
            Address = "Tallinn", City = "Tallinn", Lat = 59.4, Lng = 24.7, IsActive = true,
        });

        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "c@x.ee", City = "Tallinn",
            Category = DemandLeadCategory.Moving, Status = DemandLeadStatus.New,
            Language = "et", CreatedAt = DateTime.UtcNow,
        };
        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();

        var ok    = (await MakeAdminLeads(db).GetLeadMatches(lead.Id))
            .Should().BeOfType<OkObjectResult>().Subject;
        var items = ((System.Collections.IEnumerable)ok.Value!).Cast<object>().ToList();

        items.Should().HaveCount(2);
        // Even though the directory supplier is in the lead's city, bookable
        // listing-based matches always come first.
        Prop(items[0], "listingId").Should().Be(listing.Id);
        Prop(items[0], "isDirectory").Should().Be(false);
        Prop(items[1], "supplierId").Should().Be(dirSupplier.Id);
        Prop(items[1], "isDirectory").Should().Be(true);
    }

    // ─── GET /api/locations — public map projection ──────────────────────────

    [Fact]
    public async Task GetLocations_ExposesIsDirectory_SupplierSlug_AndServiceTypes()
    {
        var db = TestDbContext.Create();

        var dirSupplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = "Puhastus OÜ", ContactName = "C",
            ContactEmail = "p@x.ee", ContactPhone = "1", IsActive = true,
            IsDirectoryListing = true, Slug = "puhastus-ou",
            ServiceTypesJson = """["cleaning","packing"]""",
        };
        db.Suppliers.Add(dirSupplier);
        db.SupplierLocations.Add(new SupplierLocation
        {
            Id = Guid.NewGuid(), SupplierId = dirSupplier.Id, Name = "Puhastus OÜ",
            Address = "Tallinn", City = "Tallinn", Lat = 59.44, Lng = 24.75,
            IsActive = true, IsSynthetic = false,
        });

        var normalSupplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = "Ladu AS", ContactName = "C",
            ContactEmail = "l@x.ee", ContactPhone = "2", IsActive = true, Slug = "ladu-as",
        };
        db.Suppliers.Add(normalSupplier);
        var normalLocation = new SupplierLocation
        {
            Id = Guid.NewGuid(), SupplierId = normalSupplier.Id, Name = "Ladu",
            Address = "Tartu", City = "Tartu", Lat = 58.38, Lng = 26.72,
            IsActive = true, IsSynthetic = false,
        };
        db.SupplierLocations.Add(normalLocation);
        db.Listings.Add(new Listing
        {
            Id = Guid.NewGuid(), SupplierId = normalSupplier.Id,
            LocationId = normalLocation.Id, Type = ListingType.Warehouse,
            Title = "Box", City = "Tartu", IsActive = true, PriceFrom = 30m,
        });
        await db.SaveChangesAsync();

        var ok   = (await MakePublicLocations(db).GetAll(city: null, type: null))
            .Should().BeOfType<OkObjectResult>().Subject;
        var dtos = ok.Value.Should().BeAssignableTo<List<SupplierLocationDto>>().Subject;

        dtos.Should().HaveCount(2, "directory pins appear on the public map with zero units");

        var directoryPin = dtos.Single(d => d.SupplierId == dirSupplier.Id);
        directoryPin.IsDirectory.Should().BeTrue("directory supplier with zero units at the site");
        directoryPin.SupplierSlug.Should().Be("puhastus-ou");
        directoryPin.ServiceTypes.Should().Equal("cleaning", "packing");
        directoryPin.UnitCount.Should().Be(0);

        var normalPin = dtos.Single(d => d.SupplierId == normalSupplier.Id);
        normalPin.IsDirectory.Should().BeFalse();
        normalPin.SupplierSlug.Should().Be("ladu-as");
        normalPin.ServiceTypes.Should().BeEmpty();
    }
}
