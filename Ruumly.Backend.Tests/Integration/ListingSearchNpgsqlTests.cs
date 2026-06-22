using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;
using Xunit;

namespace Ruumly.Backend.Tests.Integration;

/// <summary>
/// Runs ListingService.SearchAsync against the REAL Npgsql provider (isolated temp
/// DB, per-test transaction rollback). These cover the exact behaviours EF InMemory
/// cannot reproduce and that have bitten prod / a refactor:
///  - enum string-conversion equality (`l.Type == ListingType.Warehouse` -> WHERE
///    "Type" = 'Warehouse') — the lowercase-casing prod bug class;
///  - the default-sort featured-boost ranking (HashSet.Contains -> SQL IN) after the
///    N+1 refactor;
///  - the synthetic-location gate + includeGrouped bypass.
/// If no Postgres is reachable the tests no-op (CI without a DB stays green).
/// </summary>
[Collection("Postgres integration")]
public class ListingSearchNpgsqlTests(PostgresIntegrationFixture pg)
{
    // ── Minimal stubs (mirror ListingServiceTests) ──────────────────────────
    private sealed class NoOpCache : IDistributedCache
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

    private static readonly PricingConfig Pricing = new(
        DefaultPartnerDiscountRate: 13m, DefaultVatRate: 0m, ExtrasMarginRate: 20m, RuumlyMinMarginRate: 8m,
        Starter:  new TierConfig(5m,  0m,  12m,   2,   false, false, false, "standard", "email_48h"),
        Standard: new TierConfig(8m,  49m,  8m,  10,   false, true,  false, "boosted",  "email_24h"),
        Premium:  new TierConfig(12m, 99m,  6m, 999,   true,  true,  true,  "priority", "priority_4h"));

    private sealed class NoOpPricing : IPricingConfigService
    {
        public Task<PricingConfig> GetAsync() => Task.FromResult(Pricing);
        public Task InvalidateCacheAsync() => Task.CompletedTask;
        public Task<EffectivePricing> GetEffectivePricingAsync(Supplier s) =>
            Task.FromResult(new EffectivePricing(Pricing.ForTier(s.Tier).MonthlyFee, Pricing.ForTier(s.Tier).CustomerDiscountRate));
    }

    private static ListingService Service(RuumlyDbContext db) => new(db, new NoOpCache(), new NoOpPricing());

    private static Supplier MakeSupplier(SupplierTier tier = SupplierTier.Standard) => new()
    {
        Id = Guid.NewGuid(), Name = "Sup", RegistryCode = "1", ContactName = "C",
        ContactEmail = "s@t.ee", ContactPhone = "1", IsActive = true, Tier = tier,
    };

    private static Listing MakeListing(Guid supplierId, ListingType type, Guid? locationId = null) => new()
    {
        Id = Guid.NewGuid(), SupplierId = supplierId, Type = type, Title = $"{type}",
        Address = "St", City = "Tallinn", PriceFrom = 50m, PriceUnit = "kuu",
        IsActive = true, AvailableNow = true, Description = "x", LocationId = locationId,
    };

    // ── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_TypeFilter_ReturnsOnlyThatVertical_UnderNpgsql()
    {
        if (!pg.Available) return;
        await using var db = pg.NewContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var sup = MakeSupplier();
        db.Suppliers.Add(sup);
        db.Listings.Add(MakeListing(sup.Id, ListingType.Warehouse));
        db.Listings.Add(MakeListing(sup.Id, ListingType.Moving));
        await db.SaveChangesAsync();

        // The exact comparison that broke in prod: enum -> string column equality.
        var result = await Service(db).SearchAsync(new ListingSearchRequest { Type = "warehouse" });

        result.Data.Should().HaveCount(1, "the WHERE \"Type\" = 'Warehouse' filter must match the enum-cased row");
        result.Data.Should().OnlyContain(l => l.Type == "warehouse");
    }

    [Fact]
    public async Task SearchAsync_DefaultSort_RanksActiveFeaturedBoostFirst_UnderNpgsql()
    {
        if (!pg.Available) return;
        await using var db = pg.NewContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var sup = MakeSupplier();
        db.Suppliers.Add(sup);
        var plain    = MakeListing(sup.Id, ListingType.Warehouse);
        var featured = MakeListing(sup.Id, ListingType.Warehouse);
        db.Listings.AddRange(plain, featured);

        var feature = new PaidFeature { Id = Guid.NewGuid(), Code = "featured_search", Name = "Featured" };
        db.PaidFeatures.Add(feature);
        db.SupplierPaidFeatures.Add(new SupplierPaidFeature
        {
            Id = Guid.NewGuid(), SupplierId = sup.Id, PaidFeatureId = feature.Id,
            ListingId = featured.Id, IsActive = true,
            StartsAt = DateTime.UtcNow.AddDays(-1), EndsAt = DateTime.UtcNow.AddDays(1),
        });
        await db.SaveChangesAsync();

        // Exercises the refactored default sort (HashSet.Contains -> SQL IN). Must
        // translate under Npgsql AND rank the boosted listing first.
        var result = await Service(db).SearchAsync(new ListingSearchRequest { Sort = "best" });

        result.Data.Should().HaveCount(2);
        result.Data[0].Id.Should().Be(featured.Id, "an active featured_search boost ranks first");
    }

    [Fact]
    public async Task SearchAsync_IncludeGrouped_BypassesSyntheticLocationGate_UnderNpgsql()
    {
        if (!pg.Available) return;
        await using var db = pg.NewContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var sup = MakeSupplier();
        db.Suppliers.Add(sup);
        var location = new SupplierLocation
        {
            Id = Guid.NewGuid(), SupplierId = sup.Id, Name = "Hub", Address = "St",
            City = "Tallinn", IsSynthetic = false, IsActive = true,
        };
        db.SupplierLocations.Add(location);
        db.Listings.Add(MakeListing(sup.Id, ListingType.Warehouse, locationId: location.Id));
        await db.SaveChangesAsync();

        var gated   = await Service(db).SearchAsync(new ListingSearchRequest());
        var bypass  = await Service(db).SearchAsync(new ListingSearchRequest { IncludeGrouped = true });

        gated.Data.Should().BeEmpty("a real-location listing is hidden from generic search");
        bypass.Data.Should().HaveCount(1, "includeGrouped surfaces real-location listings (homepage map/stats)");
    }
}
