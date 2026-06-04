using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

public class LocationCoordinateDriftTests
{
    private static DbContextOptions<RuumlyDbContext> SharedOptions(string dbName) =>
        new DbContextOptionsBuilder<RuumlyDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

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

    private static readonly PricingConfig DefaultPricing = new(
        DefaultPartnerDiscountRate: 13m,
        DefaultVatRate:              0m,
        ExtrasMarginRate:            20m,
        RuumlyMinMarginRate:         8m,
        Starter:  new TierConfig(5m,  0m,   12m,   2,   false, false, false, "standard", "email_48h"),
        Standard: new TierConfig(8m,  49m,   8m,  10,   false, true,  false, "boosted",  "email_24h"),
        Premium:  new TierConfig(12m, 99m,   6m, 999,   true,  true,  true,  "priority", "priority_4h"));

    private sealed class NoOpPricing : IPricingConfigService
    {
        public Task<PricingConfig> GetAsync() => Task.FromResult(DefaultPricing);
        public Task InvalidateCacheAsync() => Task.CompletedTask;
        public Task<EffectivePricing> GetEffectivePricingAsync(Supplier s) =>
            Task.FromResult(new EffectivePricing(
                DefaultPricing.ForTier(s.Tier).MonthlyFee,
                DefaultPricing.ForTier(s.Tier).CustomerDiscountRate));
    }

    [Fact(Skip = "Requires PostgreSQL provider — Npgsql ILike/GroupBy not supported by EF InMemory")]
    public async Task Location_Coordinates_Win_Over_Drifted_Listing_Snapshot()
    {
        var dbName  = Guid.NewGuid().ToString();
        var options = SharedOptions(dbName);

        var supplierId  = Guid.NewGuid();
        var locationId  = Guid.NewGuid();
        var listingId   = Guid.NewGuid();

        // ── 1. Create a SupplierLocation at (58.25, 22.48) and a child Listing ──
        using (var seedDb = new TestDbContext(options))
        {
            seedDb.Suppliers.Add(new Supplier
            {
                Id           = supplierId,
                Name         = "Saaremaa Ladu OÜ",
                RegistryCode = "EE1234",
                ContactName  = "Test",
                ContactEmail = "test@test.ee",
                ContactPhone = "+372555",
            });

            seedDb.SupplierLocations.Add(new SupplierLocation
            {
                Id         = locationId,
                SupplierId = supplierId,
                Name       = "Kuressaare Warehouse",
                Address    = "Lasteaia 7",
                City       = "Kuressaare",
                Lat        = 58.25,
                Lng        = 22.48,
            });

            seedDb.Listings.Add(new Listing
            {
                Id         = listingId,
                SupplierId = supplierId,
                LocationId = locationId,
                Type       = ListingType.Warehouse,
                Title      = "Warehouse in Kuressaare",
                Address    = "Lasteaia 7",
                City       = "Kuressaare",
                Lat        = 58.25,
                Lng        = 22.48,
                PriceFrom  = 100m,
                PriceUnit  = "kuu",
                IsActive   = true,
            });

            await seedDb.SaveChangesAsync();
        }

        // ── 2. Simulate drift: directly mutate the Listing row to (0, 0) ──
        using (var mutateDb = new TestDbContext(options))
        {
            var listing = await mutateDb.Listings.FindAsync(listingId);
            listing!.Lat = 0;
            listing.Lng  = 0;
            await mutateDb.SaveChangesAsync();
        }

        // ── 3. ListingService.GetByIdAsync should return Location coords ──
        using (var readDb = new TestDbContext(options))
        {
            var service = new ListingService(readDb, new NoOpCache(), new NoOpPricing());
            var dto = await service.GetByIdAsync(listingId);

            dto.Should().NotBeNull();
            dto!.Lat.Should().Be(58.25, "Location.Lat should override drifted Listing.Lat");
            dto.Lng.Should().Be(22.48, "Location.Lng should override drifted Listing.Lng");
        }

        // ── 4. LocationsController.GetById — units should use parent coords ──
        using (var ctrlDb = new TestDbContext(options))
        {
            var controller = new LocationsController(ctrlDb)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };

            var result = await controller.GetById(locationId);

            var okResult    = result.Should().BeOfType<OkObjectResult>().Subject;
            var locationDto = okResult.Value.Should().BeOfType<SupplierLocationDto>().Subject;

            locationDto.Lat.Should().Be(58.25);
            locationDto.Lng.Should().Be(22.48);

            locationDto.Units.Should().ContainSingle()
                .Which.Lat.Should().Be(58.25, "unit Lat should come from the parent SupplierLocation");
            locationDto.Units.Single()
                .Lng.Should().Be(22.48, "unit Lng should come from the parent SupplierLocation");
        }
    }
}
