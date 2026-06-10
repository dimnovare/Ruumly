using FluentAssertions;
using Hangfire;
using Hangfire.InMemory;
using Microsoft.AspNetCore.Http;
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
/// Validates booking-creation idempotency for sequential double-submit
/// (browser back+resubmit) and concurrent overlap races.
/// </summary>
public class BookingIdempotencyTests
{
    // ─── Infrastructure ───────────────────────────────────────────────────────

    private static RuumlyDbContext CreateDb() => TestDbContext.Create();

    private static readonly PricingConfig Config = new(
        DefaultPartnerDiscountRate: 13m,
        DefaultVatRate: 0m,
        ExtrasMarginRate: 20m,
        RuumlyMinMarginRate: 8m,
        Starter:  new TierConfig(5m,  0m,  12m,   2, false, false, false, "standard", "email_48h"),
        Standard: new TierConfig(8m,  49m,  8m,  10, false, true,  false, "boosted",  "email_24h"),
        Premium:  new TierConfig(12m, 99m,  6m, 999, true,  true,  true,  "priority", "priority_4h"));

    private static BookingService MakeService(RuumlyDbContext db) =>
        new(db,
            new StubOrderRouting(),
            new StubPricing(),
            new StubInvoice(),
            new StubHttpContext(),
            new StubCache(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BookingService>.Instance);

    private sealed class StubOrderRouting : IOrderRoutingService
    {
        public Task RouteOrderAsync(Booking b, Listing l) => Task.CompletedTask;
    }

    private sealed class StubPricing : IPricingConfigService
    {
        public Task<PricingConfig> GetAsync() => Task.FromResult(Config);
        public Task InvalidateCacheAsync() => Task.CompletedTask;
        public Task<EffectivePricing> GetEffectivePricingAsync(Supplier s) =>
            Task.FromResult(new EffectivePricing(Config.ForTier(s.Tier).MonthlyFee, Config.ForTier(s.Tier).CustomerDiscountRate));
    }

    private sealed class StubInvoice : IInvoiceService
    {
        public Task<PaginatedResult<InvoiceDto>> GetAllAsync(Guid uid, UserRole role, int page = 1, int limit = 100) =>
            Task.FromResult(new PaginatedResult<InvoiceDto>([], 0, page, limit, false));
        public Task<InvoiceDto?> GetByBookingIdAsync(Guid bid, Guid uid, UserRole role) =>
            Task.FromResult<InvoiceDto?>(null);
        public Task<InvoiceDto> GenerateAsync(Guid bid, string? paymentMethod = null) =>
            Task.FromResult(new InvoiceDto(bid, bid, 0m, "pending", "2026-01-01", null, ""));
        public Task<InvoiceDto> MarkPaidAsync(Guid id) =>
            Task.FromResult(new InvoiceDto(id, id, 0m, "paid", "2026-01-01", "2026-01-01", ""));
    }

    private sealed class StubCache : IDistributedCache
    {
        public byte[]? Get(string k) => null;
        public Task<byte[]?> GetAsync(string k, CancellationToken t = default) => Task.FromResult<byte[]?>(null);
        public void Refresh(string k) { }
        public Task RefreshAsync(string k, CancellationToken t = default) => Task.CompletedTask;
        public void Remove(string k) { }
        public Task RemoveAsync(string k, CancellationToken t = default) => Task.CompletedTask;
        public void Set(string k, byte[] v, DistributedCacheEntryOptions o) { }
        public Task SetAsync(string k, byte[] v, DistributedCacheEntryOptions o, CancellationToken t = default) => Task.CompletedTask;
    }

    private sealed class StubHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    // ─── Seed ─────────────────────────────────────────────────────────────────

    private static async Task<(Supplier supplier, Listing listing, User customer)>
        SeedAsync(RuumlyDbContext db, int quantityTotal = 1)
    {
        var supplier = new Supplier
        {
            Id              = Guid.NewGuid(),
            Name            = "TestSupplier",
            RegistryCode    = "TST001",
            ContactName     = "Contact",
            ContactEmail    = "s@test.ee",
            ContactPhone    = "+372 5000 0000",
            BillingModel    = BillingModel.Marketplace,
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

        var customer = new User
        {
            Id            = Guid.NewGuid(),
            Email         = "customer@test.ee",
            Name          = "Customer",
            Role          = UserRole.Customer,
            EmailVerified = true,
        };

        db.Suppliers.Add(supplier);
        db.Listings.Add(listing);
        db.Users.Add(customer);
        await db.SaveChangesAsync();

        return (supplier, listing, customer);
    }

    private static CreateBookingRequest MakeRequest(Guid listingId, string? idempotencyKey = null) => new()
    {
        ListingId      = listingId,
        StartDate      = "2026-09-01",
        EndDate        = "2026-10-01",
        Duration       = "1 kuu",
        Extras         = [],
        ContactName    = "Test Customer",
        ContactEmail   = "customer@test.ee",
        ContactPhone   = "+372 5000 0001",
        PaymentMethod  = "bank",
        IdempotencyKey = idempotencyKey,
    };

    // ─── Tests ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Browser back+resubmit without an idempotency key: the second sequential
    /// request must be rejected because the first booking (still Pending) already
    /// occupies the only unit. Fixed by including Pending in the overlap count.
    /// </summary>
    [Fact]
    public async Task DoubleSubmit_WithoutIdempotencyKey_SecondCallThrows()
    {
        var db  = CreateDb();
        var (_, listing, customer) = await SeedAsync(db, quantityTotal: 1);
        var svc = MakeService(db);

        // First submission succeeds
        await svc.CreateAsync(MakeRequest(listing.Id), customer.Id);

        // Second submission for same listing/dates should be rejected
        var act = async () => await svc.CreateAsync(MakeRequest(listing.Id), customer.Id);

        await act.Should().ThrowAsync<ArgumentException>(
            "the Pending booking from the first call should block the second");

        var count = await db.Bookings.CountAsync();
        count.Should().Be(1, "only one booking should exist");
    }

    /// <summary>
    /// Multi-unit listing: when quantity > 1, a second booking for the same
    /// dates is allowed until all units are taken.
    /// </summary>
    [Fact]
    public async Task DoubleSubmit_MultiUnitListing_SecondCallSucceedsWhileCapacityRemains()
    {
        var db  = CreateDb();
        var (_, listing, customer) = await SeedAsync(db, quantityTotal: 2);
        var svc = MakeService(db);

        await svc.CreateAsync(MakeRequest(listing.Id), customer.Id);
        var act = async () => await svc.CreateAsync(MakeRequest(listing.Id), customer.Id);

        await act.Should().NotThrowAsync(
            "there are 2 units; the second booking fits");

        var count = await db.Bookings.CountAsync();
        count.Should().Be(2);
    }

    /// <summary>
    /// Multi-unit listing full: once all units are filled (including Pending),
    /// further submissions are rejected.
    /// </summary>
    [Fact]
    public async Task DoubleSubmit_MultiUnitListing_ThirdCallRejectedWhenFull()
    {
        var db  = CreateDb();
        var (_, listing, customer) = await SeedAsync(db, quantityTotal: 2);
        var svc = MakeService(db);

        await svc.CreateAsync(MakeRequest(listing.Id), customer.Id);
        await svc.CreateAsync(MakeRequest(listing.Id), customer.Id);

        var act = async () => await svc.CreateAsync(MakeRequest(listing.Id), customer.Id);

        await act.Should().ThrowAsync<ArgumentException>(
            "both units are now booked (Pending counts toward capacity)");
    }

    /// <summary>
    /// Idempotency key path: duplicate call with the same key returns the
    /// original booking without creating a second one.
    /// </summary>
    [Fact]
    public async Task IdempotencyKey_SecondCallReturnsSameBooking()
    {
        var db  = CreateDb();
        var (_, listing, customer) = await SeedAsync(db);
        var key = Guid.NewGuid().ToString();
        var svc = MakeService(db);

        var first  = await svc.CreateAsync(MakeRequest(listing.Id, key), customer.Id);
        var second = await svc.CreateAsync(MakeRequest(listing.Id, key), customer.Id);

        second.Id.Should().Be(first.Id, "idempotency key must deduplicate");
        (await db.Bookings.CountAsync()).Should().Be(1);
    }

    /// <summary>
    /// Cancelled booking does not block a re-booking for the same dates.
    /// Cancelled status is excluded from the overlap count.
    /// </summary>
    [Fact]
    public async Task CancelledBooking_DoesNotBlockRebooking()
    {
        var db  = CreateDb();
        var (supplier, listing, customer) = await SeedAsync(db, quantityTotal: 1);

        // Seed a cancelled booking occupying the same date range
        db.Bookings.Add(new Booking
        {
            Id         = Guid.NewGuid(),
            UserId     = customer.Id,
            ListingId  = listing.Id,
            SupplierId = supplier.Id,
            StartDate  = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate    = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            Duration   = "1 kuu",
            Status     = BookingStatus.Cancelled,
        });
        await db.SaveChangesAsync();

        var svc = MakeService(db);
        var act = async () => await svc.CreateAsync(MakeRequest(listing.Id), customer.Id);

        await act.Should().NotThrowAsync(
            "cancelled bookings must not block new bookings for the same dates");
    }
}
