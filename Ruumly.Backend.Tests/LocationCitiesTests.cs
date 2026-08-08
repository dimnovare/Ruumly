using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Models;

namespace Ruumly.Backend.Tests;

/// <summary>
/// GET /api/locations/cities — the city dropdown on the public search page.
///
/// This endpoint used to read Listings alone. In production the supply is the
/// provider DIRECTORY (SupplierLocations with zero listings), so the dropdown
/// came back EMPTY and "All cities" was the only option a visitor ever saw.
/// These tests pin the union (directory + listings) and the per-service scope.
/// </summary>
public class LocationCitiesTests
{
    private static LocationsController MakePublic(RuumlyDbContext db) =>
        // No identity — the anonymous public search view.
        new(db) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    private static Supplier Directory(string name, string slug, string serviceTypesJson) => new()
    {
        Id = Guid.NewGuid(), Name = name, Slug = slug, Country = "EE",
        ContactName = name, ContactEmail = $"info@{slug}.ee",
        IsActive = true, IsDirectoryListing = true, IsPartnerPagePublished = true,
        ServiceTypesJson = serviceTypesJson,
    };

    private static SupplierLocation Location(Supplier s, string city, bool isSynthetic = false) => new()
    {
        Id = Guid.NewGuid(), SupplierId = s.Id, Name = s.Name,
        Address = city, City = city, Lat = 59.4, Lng = 24.7,
        IsActive = true, IsSynthetic = isSynthetic,
    };

    private static List<string> Cities(IActionResult result) =>
        ((System.Collections.IEnumerable)((OkObjectResult)result).Value!)
            .Cast<object>()
            .Select(o => (string)o.GetType().GetProperty("City")!.GetValue(o)!)
            .ToList();

    [Fact]
    public async Task Cities_IncludeDirectoryProfiles_ThatHaveNoListings()
    {
        var db = TestDbContext.Create();
        var s  = Directory("Kapsel Minilaod", "kapsel-minilaod", "[\"warehouse\"]");
        db.Suppliers.Add(s);
        db.SupplierLocations.Add(Location(s, "Tartu"));
        await db.SaveChangesAsync();

        var cities = Cities(await MakePublic(db).GetCities());

        cities.Should().Equal(["Tartu"],
            "the directory is the production supply — reading Listings alone returns an empty dropdown");
    }

    [Fact]
    public async Task Cities_ScopedToService_ExcludeProvidersOfOtherServices()
    {
        var db = TestDbContext.Create();
        var storage  = Directory("Laod OÜ", "laod-ou", "[\"warehouse\"]");
        var cleaning = Directory("Puhastus OÜ", "puhastus-ou", "[\"cleaning\"]");
        db.Suppliers.AddRange(storage, cleaning);
        db.SupplierLocations.AddRange(Location(storage, "Tartu"), Location(cleaning, "Pärnu"));
        await db.SaveChangesAsync();

        Cities(await MakePublic(db).GetCities("warehouse")).Should().Equal("Tartu");
        // Directory-only category (no ListingType) still resolves via ServiceTypesJson.
        Cities(await MakePublic(db).GetCities("cleaning")).Should().Equal("Pärnu");
        Cities(await MakePublic(db).GetCities()).Should().Equal("Pärnu", "Tartu");
    }

    [Fact]
    public async Task Cities_HideSynthetic_InactiveAndUnknownSlugs()
    {
        var db = TestDbContext.Create();
        var visible  = Directory("Nähtav", "nahtav", "[\"warehouse\"]");
        var hidden   = Directory("Peidus", "peidus", "[\"warehouse\"]");
        hidden.IsActive = false;
        db.Suppliers.AddRange(visible, hidden);
        db.SupplierLocations.AddRange(
            Location(visible, "Tallinn"),
            // Synthetic locations are internal grouping rows, never public geography.
            Location(visible, "Kohtla-Järve", isSynthetic: true),
            Location(hidden,  "Narva"));
        await db.SaveChangesAsync();

        Cities(await MakePublic(db).GetCities()).Should().Equal("Tallinn");
        Cities(await MakePublic(db).GetCities("not-a-service")).Should().BeEmpty();
    }

    // 2026-08: packing and insurance stopped being consumer-facing services, but
    // their city hubs (/{lang}/search?type=packing|insurance&city=X) are LIVE AND
    // INDEXED. Those requests must keep resolving to something real instead of
    // suddenly returning an empty dropdown to a visitor arriving from Google.
    [Fact]
    public async Task Cities_RetiredServiceSlugs_ResolveInsteadOfDeadEnding()
    {
        var db      = TestDbContext.Create();
        var mover   = Directory("Kolimine OÜ", "kolimine-ou", "[\"moving\",\"packing\"]");
        var cleaner = Directory("Puhastus OÜ", "puhastus-ou", "[\"cleaning\"]");
        db.Suppliers.AddRange(mover, cleaner);
        db.SupplierLocations.AddRange(Location(mover, "Tallinn"), Location(cleaner, "Pärnu"));
        await db.SaveChangesAsync();

        Cities(await MakePublic(db).GetCities("packing")).Should().Equal(["Tallinn"],
            "packing resolves to the moving pool — the movers who actually do the packing");
        Cities(await MakePublic(db).GetCities("insurance")).Should().Equal(["Pärnu", "Tallinn"],
            "insurance has no consumer equivalent, so it falls back to the generic city list");
        Cities(await MakePublic(db).GetCities("not-a-service")).Should().BeEmpty(
            "an unknown slug still means nothing can match — only KNOWN retired slugs are rewritten");
    }

    [Fact]
    public async Task Cities_DedupeCaseInsensitively_AcrossBothPools()
    {
        var db = TestDbContext.Create();
        var a = Directory("Ladu A", "ladu-a", "[\"warehouse\"]");
        var b = Directory("Ladu B", "ladu-b", "[\"warehouse\"]");
        db.Suppliers.AddRange(a, b);
        db.SupplierLocations.AddRange(Location(a, "Tartu"), Location(b, "tartu"));
        await db.SaveChangesAsync();

        Cities(await MakePublic(db).GetCities()).Should().Equal(["Tartu"],
            "one city must not appear twice because two providers cased it differently");
    }
}
