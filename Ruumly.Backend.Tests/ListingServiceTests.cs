using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;

namespace Ruumly.Backend.Tests;

public class ListingServiceTests
{
    // ─── Test infrastructure ───────────────────────────────────────────────

    private static RuumlyDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<RuumlyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RuumlyDbContext(opts);
    }

    private static ListingService MakeService(RuumlyDbContext db) =>
        new(db, new NoOpDistributedCache());

    private sealed class NoOpDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.CompletedTask;
    }

    private static Supplier MakeSupplier(string suffix = "A") => new()
    {
        Id           = Guid.NewGuid(),
        Name         = $"Supplier {suffix}",
        RegistryCode = suffix,
        ContactName  = suffix,
        ContactEmail = $"{suffix.ToLower()}@test.ee",
        ContactPhone = "1",
    };

    private static Listing MakeListing(
        Guid        supplierId,
        string      city        = "Tallinn",
        ListingType type        = ListingType.Warehouse,
        bool        isActive    = true,
        decimal     priceFrom   = 50m,
        ListingBadge? badge     = null) => new()
    {
        Id           = Guid.NewGuid(),
        SupplierId   = supplierId,
        Type         = type,
        Title        = $"{type} in {city}",
        Address      = $"Test St, {city}",
        City         = city,
        PriceFrom    = priceFrom,
        PriceUnit    = "kuu",
        IsActive     = isActive,
        AvailableNow = true,
        Description  = "Test listing",
        Badge        = badge,
    };

    private static async Task<RuumlyDbContext> SeedListingsAsync(
        params (ListingType type, string city, bool isActive, decimal price, ListingBadge? badge)[] specs)
    {
        var db       = CreateDb();
        var supplier = MakeSupplier();
        db.Suppliers.Add(supplier);

        foreach (var (type, city, isActive, price, badge) in specs)
            db.Listings.Add(MakeListing(supplier.Id, city, type, isActive, price, badge));

        await db.SaveChangesAsync();
        return db;
    }

    // ─── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_Filters_By_Type()
    {
        var db = await SeedListingsAsync(
            (ListingType.Warehouse, "Tallinn", true, 50m, null),
            (ListingType.Warehouse, "Tallinn", true, 60m, null),
            (ListingType.Moving,    "Tallinn", true, 40m, null));

        var service = MakeService(db);

        var result = await service.SearchAsync(new ListingSearchRequest { Type = "warehouse" });

        result.Data.Should().HaveCount(2);
        result.Data.Should().OnlyContain(l => l.Type == "warehouse");
    }

    [Fact]
    public async Task SearchAsync_Filters_By_City_Case_Insensitive()
    {
        var db = await SeedListingsAsync(
            (ListingType.Warehouse, "Tallinn", true, 50m, null),
            (ListingType.Warehouse, "Tartu",   true, 50m, null),
            (ListingType.Warehouse, "tallinn", true, 50m, null)); // lowercase variant

        var service = MakeService(db);

        var result = await service.SearchAsync(new ListingSearchRequest { City = "TALLINN" });

        result.Data.Should().HaveCount(2);
        result.Data.Should().OnlyContain(l => l.City.ToLower().Contains("tallinn"));
    }

    [Fact]
    public async Task SearchAsync_Excludes_Inactive_Listings()
    {
        var db = await SeedListingsAsync(
            (ListingType.Warehouse, "Tallinn", true,  50m, null),
            (ListingType.Warehouse, "Tallinn", false, 50m, null),  // inactive
            (ListingType.Warehouse, "Tallinn", false, 50m, null)); // inactive

        var service = MakeService(db);

        var result = await service.SearchAsync(new ListingSearchRequest());

        result.Data.Should().HaveCount(1);
    }

    [Fact]
    public async Task SearchAsync_Paginates_Correctly()
    {
        // Seed 5 listings
        var db = await SeedListingsAsync(
            (ListingType.Warehouse, "Tallinn", true, 10m, null),
            (ListingType.Warehouse, "Tallinn", true, 20m, null),
            (ListingType.Warehouse, "Tallinn", true, 30m, null),
            (ListingType.Warehouse, "Tallinn", true, 40m, null),
            (ListingType.Warehouse, "Tallinn", true, 50m, null));

        var service = MakeService(db);

        var page1 = await service.SearchAsync(new ListingSearchRequest { Page = 1, Limit = 2 });
        var page2 = await service.SearchAsync(new ListingSearchRequest { Page = 2, Limit = 2 });
        var page3 = await service.SearchAsync(new ListingSearchRequest { Page = 3, Limit = 2 });

        page1.Data.Should().HaveCount(2);
        page1.Total.Should().Be(5);
        page1.HasMore.Should().BeTrue();

        page2.Data.Should().HaveCount(2);
        page2.HasMore.Should().BeTrue();

        page3.Data.Should().HaveCount(1);
        page3.HasMore.Should().BeFalse();

        // Pages should not overlap
        var allIds = page1.Data.Select(l => l.Id)
            .Concat(page2.Data.Select(l => l.Id))
            .Concat(page3.Data.Select(l => l.Id));
        allIds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task GetFeaturedAsync_Returns_Max_4_Badged_Listings()
    {
        var db = await SeedListingsAsync(
            (ListingType.Warehouse, "Tallinn", true, 10m, ListingBadge.Promoted),
            (ListingType.Warehouse, "Tallinn", true, 20m, ListingBadge.BestValue),
            (ListingType.Warehouse, "Tallinn", true, 30m, ListingBadge.Closest),
            (ListingType.Warehouse, "Tallinn", true, 40m, ListingBadge.Cheapest),
            (ListingType.Warehouse, "Tallinn", true, 50m, ListingBadge.Promoted), // 5th badged
            (ListingType.Warehouse, "Tallinn", true, 60m, null));                 // no badge

        var service = MakeService(db);

        var featured = await service.GetFeaturedAsync();

        featured.Should().HaveCount(4);
        featured.Should().OnlyContain(l => l.Badge != null);
    }

    [Fact]
    public async Task GetFeaturedAsync_Orders_By_Badge_Priority()
    {
        // Promoted > BestValue > Closest > Cheapest
        var db = await SeedListingsAsync(
            (ListingType.Warehouse, "Tallinn", true, 10m, ListingBadge.Cheapest),
            (ListingType.Warehouse, "Tallinn", true, 20m, ListingBadge.Closest),
            (ListingType.Warehouse, "Tallinn", true, 30m, ListingBadge.BestValue),
            (ListingType.Warehouse, "Tallinn", true, 40m, ListingBadge.Promoted));

        var service  = MakeService(db);
        var featured = await service.GetFeaturedAsync();

        featured.Should().HaveCount(4);
        featured[0].Badge.Should().Be("promoted");
        featured[1].Badge.Should().Be("best-value");
        featured[2].Badge.Should().Be("closest");
        featured[3].Badge.Should().Be("cheapest");
    }

    [Fact]
    public async Task GetByIdAsync_Returns_Null_For_Inactive_Listing()
    {
        var db       = CreateDb();
        var supplier = MakeSupplier();
        db.Suppliers.Add(supplier);
        var listing = MakeListing(supplier.Id, isActive: false);
        db.Listings.Add(listing);
        await db.SaveChangesAsync();

        var service = MakeService(db);

        var result = await service.GetByIdAsync(listing.Id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_Returns_Listing_When_Active()
    {
        var db       = CreateDb();
        var supplier = MakeSupplier();
        db.Suppliers.Add(supplier);
        var listing = MakeListing(supplier.Id, isActive: true);
        db.Listings.Add(listing);
        await db.SaveChangesAsync();

        var service = MakeService(db);

        var result = await service.GetByIdAsync(listing.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(listing.Id);
    }
}
