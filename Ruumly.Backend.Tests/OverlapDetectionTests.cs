using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Tests the booking overlap logic in BookingService.CreateAsync.
/// The overlap check counts Confirmed/Active bookings on the same listing that
/// overlap the requested date range and rejects if count >= listing.QuantityTotal.
/// </summary>
public class OverlapDetectionTests
{
    // ─── Test infrastructure ──────────────────────────────────────────────────

    private static RuumlyDbContext CreateDb() => TestDbContext.Create();

    private static readonly PricingConfig TestPricingConfig = new(
        DefaultPartnerDiscountRate: 13m,
        DefaultVatRate:              0m,
        ExtrasMarginRate:            20m,
        RuumlyMinMarginRate:         8m,
        Starter:  new TierConfig(5m,  0m,   12m,   2,   false, false, false, "standard",  "email_48h"),
        Standard: new TierConfig(8m,  49m,   8m,  10,   false, true,  false, "boosted",   "email_24h"),
        Premium:  new TierConfig(12m, 99m,   6m, 999,   true,  true,  true,  "priority",  "priority_4h"));

    private static BookingService MakeService(RuumlyDbContext db) =>
        new(db,
            new NoOpRouting(),
            new NoOpPricing(),
            new NoOpInvoice(),
            new NoOpHttp(),
            new NoOpCache(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BookingService>.Instance);

    private sealed class NoOpRouting : IOrderRoutingService
    {
        public Task RouteOrderAsync(Booking b, Listing l) => Task.CompletedTask;
    }

    private sealed class NoOpPricing : IPricingConfigService
    {
        public Task<PricingConfig> GetAsync() => Task.FromResult(TestPricingConfig);
        public Task InvalidateCacheAsync() => Task.CompletedTask;
        public Task<EffectivePricing> GetEffectivePricingAsync(Ruumly.Backend.Models.Supplier s) =>
            Task.FromResult(new EffectivePricing(TestPricingConfig.ForTier(s.Tier).MonthlyFee, TestPricingConfig.ForTier(s.Tier).CustomerDiscountRate));
    }

    private sealed class NoOpInvoice : IInvoiceService
    {
        public Task<PaginatedResult<InvoiceDto>> GetAllAsync(Guid userId, UserRole role, int page = 1, int limit = 100) => Task.FromResult(new PaginatedResult<InvoiceDto>(new List<InvoiceDto>(), 0, page, limit, false));
        public Task<InvoiceDto?> GetByBookingIdAsync(Guid bookingId, Guid userId, UserRole role) => Task.FromResult<InvoiceDto?>(null);
        public Task<InvoiceDto> GenerateAsync(Guid bookingId) =>
            Task.FromResult(new InvoiceDto(bookingId, bookingId, 0m, "pending", "2026-01-01", null, ""));
        public Task<InvoiceDto> MarkPaidAsync(Guid id) =>
            Task.FromResult(new InvoiceDto(id, id, 0m, "paid", "2026-01-01", "2026-01-01", ""));
    }

    private sealed class NoOpHttp : Microsoft.AspNetCore.Http.IHttpContextAccessor
    {
        public Microsoft.AspNetCore.Http.HttpContext? HttpContext { get; set; }
    }

    private sealed class NoOpCache : IDistributedCache
    {
        public byte[]? Get(string key) => null;
        public Task<byte[]?> GetAsync(string key, CancellationToken t = default) => Task.FromResult<byte[]?>(null);
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken t = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken t = default) => Task.CompletedTask;
        public void Set(string key, byte[] value, DistributedCacheEntryOptions o) { }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions o, CancellationToken t = default) => Task.CompletedTask;
    }

    // ─── Seed helpers ─────────────────────────────────────────────────────────

    private static async Task<(Supplier supplier, Listing listing, User user)> SeedAsync(
        RuumlyDbContext db, int quantityTotal = 1)
    {
        var supplier = new Supplier
        {
            Id           = Guid.NewGuid(),
            Name         = "TestSupplier",
            RegistryCode = "TST001",
            ContactName  = "Contact",
            ContactEmail = "supplier@test.ee",
            ContactPhone = "+372 5000 0000",
        };

        var listing = new Listing
        {
            Id             = Guid.NewGuid(),
            SupplierId     = supplier.Id,
            Type           = ListingType.Warehouse,
            Title          = "Test Warehouse",
            Address        = "Test St 1",
            City           = "Tallinn",
            PriceFrom      = 100m,
            PriceUnit      = "kuu",
            IsActive       = true,
            AvailableNow   = true,
            Description    = "Test",
            VatRate        = 0m,
            QuantityTotal  = quantityTotal,
        };

        var user = new User
        {
            Id            = Guid.NewGuid(),
            Email         = "customer@test.ee",
            Name          = "Customer",
            Role          = UserRole.Customer,
            EmailVerified = true,
        };

        db.Suppliers.Add(supplier);
        db.Listings.Add(listing);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (supplier, listing, user);
    }

    // Directly seeds a confirmed booking (bypassing service), simulating an
    // already-confirmed reservation that occupies capacity.
    private static async Task SeedConfirmedBookingAsync(
        RuumlyDbContext db,
        Guid listingId,
        Guid supplierId,
        Guid userId,
        DateTime start,
        DateTime end)
    {
        db.Bookings.Add(new Booking
        {
            Id         = Guid.NewGuid(),
            UserId     = userId,
            ListingId  = listingId,
            SupplierId = supplierId,
            StartDate  = start,
            EndDate    = end,
            Duration   = "1 kuu",
            Status     = BookingStatus.Confirmed,
        });
        await db.SaveChangesAsync();
    }

    private static CreateBookingRequest MakeRequest(Guid listingId, string startDate, string endDate) => new()
    {
        ListingId    = listingId,
        StartDate    = startDate,
        EndDate      = endDate,
        Duration     = "1 kuu",
        Extras       = [],
        ContactName  = "Test Customer",
        ContactEmail = "customer@test.ee",
        ContactPhone = "+372 5000 0001",
    };

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Overlap_SameUnit_OverlappingDates_IsRejected()
    {
        // Jan 1–31 already confirmed; trying Jan 15–Feb 15 overlaps → reject.
        var db = CreateDb();
        var (supplier, listing, user) = await SeedAsync(db, quantityTotal: 1);

        await SeedConfirmedBookingAsync(db, listing.Id, supplier.Id, user.Id,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc));

        var act = async () => await MakeService(db).CreateAsync(
            MakeRequest(listing.Id, "2026-01-15", "2026-02-15"), user.Id);

        await act.Should().ThrowAsync<ArgumentException>("unit is fully booked for the overlapping period");
    }

    [Fact]
    public async Task Overlap_SameUnit_AdjacentDates_Succeeds()
    {
        // Jan 1–31 confirmed; trying Feb 1–28 is adjacent (no overlap) → succeed.
        var db = CreateDb();
        var (supplier, listing, user) = await SeedAsync(db, quantityTotal: 1);

        await SeedConfirmedBookingAsync(db, listing.Id, supplier.Id, user.Id,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc));

        var act = async () => await MakeService(db).CreateAsync(
            MakeRequest(listing.Id, "2026-02-01", "2026-02-28"), user.Id);

        await act.Should().NotThrowAsync("Feb 1 does not overlap with a booking that ends Jan 31");
    }

    [Fact]
    public async Task Overlap_TwoUnits_BothBooked_Succeed()
    {
        // QuantityTotal=2; one unit already confirmed Jan 1–31.
        // Second booking for the same dates should succeed (1 < 2).
        var db = CreateDb();
        var (supplier, listing, user) = await SeedAsync(db, quantityTotal: 2);

        await SeedConfirmedBookingAsync(db, listing.Id, supplier.Id, user.Id,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc));

        var act = async () => await MakeService(db).CreateAsync(
            MakeRequest(listing.Id, "2026-01-01", "2026-01-31"), user.Id);

        await act.Should().NotThrowAsync("one slot is free out of two total units");
    }

    [Fact]
    public async Task Overlap_TwoUnits_AtCapacity_ThirdBookingRejected()
    {
        // QuantityTotal=2; two units already confirmed Jan 1–31.
        // Third booking for the same dates must be rejected (2 >= 2).
        var db = CreateDb();
        var (supplier, listing, user) = await SeedAsync(db, quantityTotal: 2);

        await SeedConfirmedBookingAsync(db, listing.Id, supplier.Id, user.Id,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc));

        await SeedConfirmedBookingAsync(db, listing.Id, supplier.Id, user.Id,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc));

        var act = async () => await MakeService(db).CreateAsync(
            MakeRequest(listing.Id, "2026-01-01", "2026-01-31"), user.Id);

        await act.Should().ThrowAsync<ArgumentException>("both units are already taken");
    }
}
