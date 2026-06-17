using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

public class BookingServiceTests
{
    // ─── Test infrastructure ───────────────────────────────────────────────

    private static RuumlyDbContext CreateDb() => TestDbContext.Create();

    // Config: partner=13%, minMargin=8% → customerDiscount=5% → platformPrice=95 on base 100.
    // VAT=0% so totals stay clean.
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
            new NoOpOrderRoutingService(),
            new NoOpPricingConfigService(),
            new NoOpInvoiceService(),
            new NoOpHttpContextAccessor(),
            new NoOpDistributedCache(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BookingService>.Instance);

    private sealed class NoOpOrderRoutingService : IOrderRoutingService
    {
        public Task RouteOrderAsync(Booking booking, Listing listing) => Task.CompletedTask;
    }

    private sealed class NoOpPricingConfigService : IPricingConfigService
    {
        public Task<PricingConfig> GetAsync() => Task.FromResult(TestPricingConfig);
        public Task InvalidateCacheAsync() => Task.CompletedTask;
        public Task<EffectivePricing> GetEffectivePricingAsync(Ruumly.Backend.Models.Supplier s) =>
            Task.FromResult(new EffectivePricing(TestPricingConfig.ForTier(s.Tier).MonthlyFee, TestPricingConfig.ForTier(s.Tier).CustomerDiscountRate));
    }

    private sealed class NoOpInvoiceService : IInvoiceService
    {
        public Task<PaginatedResult<InvoiceDto>> GetAllAsync(Guid userId, UserRole role, int page = 1, int limit = 100) => Task.FromResult(new PaginatedResult<InvoiceDto>(new List<InvoiceDto>(), 0, page, limit, false));
        public Task<InvoiceDto?> GetByBookingIdAsync(Guid bookingId, Guid userId, UserRole role) => Task.FromResult<InvoiceDto?>(null);
        public Task<InvoiceDto> GenerateAsync(Guid bookingId, string? paymentMethod = null) =>
            Task.FromResult(new InvoiceDto(bookingId, bookingId, 0m, "pending", "2026-01-01", null, ""));
        public Task<InvoiceDto> MarkPaidAsync(Guid id) =>
            Task.FromResult(new InvoiceDto(id, id, 0m, "paid", "2026-01-01", "2026-01-01", ""));
    }

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

    private sealed class NoOpHttpContextAccessor : Microsoft.AspNetCore.Http.IHttpContextAccessor
    {
        public Microsoft.AspNetCore.Http.HttpContext? HttpContext { get; set; }
    }

    // ─── Seed helpers ──────────────────────────────────────────────────────

    private static async Task<(Supplier supplier, Listing listing, User user)> SeedBasicAsync(
        RuumlyDbContext db,
        decimal priceFrom  = 100m,
        bool    isActive   = true,
        bool    bookingEnabled = true)
    {
        var supplier = new Supplier
        {
            Id                = Guid.NewGuid(),
            Name              = "TestSupplier",
            RegistryCode      = "123456",
            ContactName       = "Contact",
            ContactEmail      = "supplier@test.ee",
            ContactPhone      = "+372 5000 0000",
            BookingEnabled    = bookingEnabled,
        };

        var listing = new Listing
        {
            Id           = Guid.NewGuid(),
            SupplierId   = supplier.Id,
            Type         = ListingType.Warehouse,
            Title        = "Test Warehouse",
            Address      = "Test St 1",
            City         = "Tallinn",
            PriceFrom    = priceFrom,
            PriceUnit    = "kuu",
            IsActive     = isActive,
            AvailableNow = true,
            Description  = "Test",
            VatRate      = 0m,   // explicit zero so totals stay predictable in tests
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

    [Fact]
    public async Task CreateAsync_Rejects_When_Supplier_Booking_Disabled()
    {
        await using var db = CreateDb();
        var (_, listing, user) = await SeedBasicAsync(db, bookingEnabled: false);
        var service = MakeService(db);

        var act = async () => await service.CreateAsync(MakeBookingRequest(listing.Id), user.Id);

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage(ErrorMessages.Get("BOOKING_DISABLED", "et"));
    }

    private static async Task SeedExtrasAsync(RuumlyDbContext db, Guid listingId, params (string Key, decimal CustomerPrice)[] extras)
    {
        foreach (var (key, price) in extras)
        {
            db.ListingExtras.Add(new ListingExtra
            {
                Id            = Guid.NewGuid(),
                ListingId     = listingId,
                Key           = key,
                Label         = key,
                PublicPrice   = price,
                SupplierPrice = Math.Round(price * 0.85m, 2),
                CustomerPrice = price,
                IsActive      = true,
            });
        }
        await db.SaveChangesAsync();
    }

    private static CreateBookingRequest MakeBookingRequest(
        Guid listingId,
        string startDate  = "2027-09-01",
        string? endDate   = null,
        List<string>? extras = null) =>
        new()
        {
            ListingId    = listingId,
            StartDate    = startDate,
            EndDate      = endDate,
            Duration     = "1 kuu",
            Extras       = extras ?? [],
            ContactName  = "Test Customer",
            ContactEmail = "customer@test.ee",
            ContactPhone = "+372 5000 0001",
        };

    // ─── Pricing tests ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_Calculates_PlatformPrice_As_95pct_Of_BasePrice()
    {
        // partner=13% (default), minMargin=8% → customerDiscount=5% → platformPrice=95
        var db = CreateDb();
        var (_, listing, user) = await SeedBasicAsync(db, priceFrom: 100m);
        var service = MakeService(db);

        var booking = await service.CreateAsync(
            MakeBookingRequest(listing.Id), user.Id);

        booking.BasePrice.Should().Be(100m);
        booking.PlatformPrice.Should().Be(95m);
        booking.ExtrasTotal.Should().Be(0m);
        booking.Total.Should().Be(95m);
    }

    [Fact]
    public async Task CreateAsync_Rounds_Discounted_Prices_To_Cents()
    {
        var db = CreateDb();
        var (_, listing, user) = await SeedBasicAsync(db, priceFrom: 1m);
        var service = MakeService(db);

        var booking = await service.CreateAsync(
            MakeBookingRequest(
                listing.Id,
                startDate: "2027-06-11",
                endDate: "2027-07-11"),
            user.Id);

        booking.BasePrice.Should().Be(0.99m);
        booking.PlatformPrice.Should().Be(0.94m);
        booking.PlatformPrice.Should().BeLessThanOrEqualTo(booking.BasePrice);
    }

    [Fact]
    public async Task CreateAsync_Calculates_Extras_Totals_Correctly()
    {
        var db = CreateDb();
        var (_, listing, user) = await SeedBasicAsync(db, priceFrom: 100m);
        await SeedExtrasAsync(db, listing.Id, ("packing", 15m), ("loading", 20m));
        var service = MakeService(db);

        var booking = await service.CreateAsync(
            MakeBookingRequest(listing.Id, extras: ["packing", "loading"]),
            user.Id);

        booking.PlatformPrice.Should().Be(95m);
        booking.ExtrasTotal.Should().Be(35m);
        booking.Total.Should().Be(130m);
    }

    [Fact]
    public async Task CreateAsync_All_Known_Extras_Prices_Match()
    {
        var db = CreateDb();
        var (_, listing, user) = await SeedBasicAsync(db, priceFrom: 100m);
        await SeedExtrasAsync(db, listing.Id,
            ("packing",   15m),
            ("loading",   20m),
            ("insurance", 10m),
            ("forklift",  25m));
        var service = MakeService(db);

        var booking = await service.CreateAsync(
            MakeBookingRequest(listing.Id, extras: ["packing", "loading", "insurance", "forklift"]),
            user.Id);

        booking.ExtrasTotal.Should().Be(70m);
        booking.Total.Should().Be(165m);  // 95 + 70
    }

    [Fact]
    public async Task CreateAsync_Rejects_Inactive_Listing()
    {
        var db = CreateDb();
        var (_, listing, user) = await SeedBasicAsync(db, isActive: false);
        var service = MakeService(db);

        var act = async () => await service.CreateAsync(MakeBookingRequest(listing.Id), user.Id);

        await act.Should().ThrowAsync<Exception>().WithMessage("*");
    }

    [Fact]
    public async Task CreateAsync_Rejects_Overlapping_Booking_When_At_Capacity()
    {
        var db = CreateDb();
        var (supplier, listing, user) = await SeedBasicAsync(db, priceFrom: 100m);

        db.Bookings.Add(new Booking
        {
            Id         = Guid.NewGuid(),
            UserId     = user.Id,
            ListingId  = listing.Id,
            SupplierId = supplier.Id,
            StartDate  = new DateTime(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate    = new DateTime(2027, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            Duration   = "1 kuu",
            Status     = BookingStatus.Confirmed,
        });
        await db.SaveChangesAsync();
        var service = MakeService(db);

        var act = async () => await service.CreateAsync(
            MakeBookingRequest(listing.Id, startDate: "2027-09-15", endDate: "2027-09-25"),
            user.Id);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ─── Open-ended overbooking tests ──────────────────────────────────────
    // Storage is open-ended/monthly (null EndDate = ongoing). The capacity guard
    // must treat a null EndDate as +infinity on BOTH sides so open-ended bookings
    // can't skip the overlap check and overbook a single-unit listing.

    [Fact]
    public async Task CreateAsync_Rejects_Second_OpenEnded_Booking_On_SingleUnit_Listing()
    {
        var db = CreateDb();
        var (supplier, listing, user) = await SeedBasicAsync(db, priceFrom: 100m);
        listing.QuantityTotal = 1;
        await db.SaveChangesAsync();
        var service = MakeService(db);

        // First open-ended booking (StartDate set, no EndDate) succeeds.
        var first = await service.CreateAsync(
            MakeBookingRequest(listing.Id, startDate: "2027-09-01", endDate: null),
            user.Id);
        first.Should().NotBeNull();

        // Second open-ended booking on the same single-unit listing must be rejected:
        // the existing open-ended row (EndDate == null) is still occupying the unit.
        var act = async () => await service.CreateAsync(
            MakeBookingRequest(listing.Id, startDate: "2027-10-01", endDate: null),
            user.Id);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateAsync_Rejects_New_Dated_Booking_Overlapping_Existing_OpenEnded()
    {
        var db = CreateDb();
        var (supplier, listing, user) = await SeedBasicAsync(db, priceFrom: 100m);
        listing.QuantityTotal = 1;
        await db.SaveChangesAsync();

        // Existing open-ended booking (no EndDate = runs to +infinity).
        db.Bookings.Add(new Booking
        {
            Id         = Guid.NewGuid(),
            UserId     = user.Id,
            ListingId  = listing.Id,
            SupplierId = supplier.Id,
            StartDate  = new DateTime(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate    = null,
            Duration   = "1 kuu",
            Status     = BookingStatus.Confirmed,
        });
        await db.SaveChangesAsync();
        var service = MakeService(db);

        // A new DATED booking starting after the open-ended booking's start overlaps it
        // (open-ended runs forever), so it must be rejected on a single-unit listing.
        var act = async () => await service.CreateAsync(
            MakeBookingRequest(listing.Id, startDate: "2027-12-01", endDate: "2027-12-31"),
            user.Id);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateAsync_Allows_Single_OpenEnded_Booking_On_Free_SingleUnit_Listing()
    {
        var db = CreateDb();
        var (_, listing, user) = await SeedBasicAsync(db, priceFrom: 100m);
        listing.QuantityTotal = 1;
        await db.SaveChangesAsync();
        var service = MakeService(db);

        var booking = await service.CreateAsync(
            MakeBookingRequest(listing.Id, startDate: "2027-09-01", endDate: null),
            user.Id);

        booking.Should().NotBeNull();
    }

    // ─── Role scoping tests ────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_Customer_Sees_Only_Own_Bookings()
    {
        var db = CreateDb();
        var (supplier, listing, userA) = await SeedBasicAsync(db);

        var userB = new User
        {
            Id    = Guid.NewGuid(),
            Email = "other@test.ee",
            Name  = "Other",
            Role  = UserRole.Customer,
        };
        db.Users.Add(userB);

        db.Bookings.AddRange(
            new Booking { Id = Guid.NewGuid(), UserId = userA.Id, ListingId = listing.Id, SupplierId = supplier.Id, StartDate = DateTime.UtcNow, Duration = "1d" },
            new Booking { Id = Guid.NewGuid(), UserId = userB.Id, ListingId = listing.Id, SupplierId = supplier.Id, StartDate = DateTime.UtcNow, Duration = "1d" });
        await db.SaveChangesAsync();

        var service = MakeService(db);

        var resultA = await service.GetAllAsync(userA.Id, UserRole.Customer);
        var resultB = await service.GetAllAsync(userB.Id, UserRole.Customer);

        resultA.Data.Should().HaveCount(1);
        resultB.Data.Should().HaveCount(1);
        resultA.Data.Select(b => b.Id).Should().NotIntersectWith(resultB.Data.Select(b => b.Id));
    }

    [Fact]
    public async Task GetAllAsync_Provider_Sees_Only_Own_Supplier_Bookings()
    {
        var db = CreateDb();

        var suppA = new Supplier { Id = Guid.NewGuid(), Name = "A", RegistryCode = "A", ContactName = "A", ContactEmail = "a@a.ee", ContactPhone = "1" };
        var suppB = new Supplier { Id = Guid.NewGuid(), Name = "B", RegistryCode = "B", ContactName = "B", ContactEmail = "b@b.ee", ContactPhone = "2" };

        var listA = new Listing { Id = Guid.NewGuid(), SupplierId = suppA.Id, Type = ListingType.Warehouse, Title = "A", Address = "A", City = "A", PriceUnit = "kuu", Description = "A" };
        var listB = new Listing { Id = Guid.NewGuid(), SupplierId = suppB.Id, Type = ListingType.Warehouse, Title = "B", Address = "B", City = "B", PriceUnit = "kuu", Description = "B" };

        var customer  = new User { Id = Guid.NewGuid(), Email = "cust@test.ee",  Name = "Cust",  Role = UserRole.Customer };
        var providerA = new User { Id = Guid.NewGuid(), Email = "provA@test.ee", Name = "ProvA", Role = UserRole.Provider, SupplierId = suppA.Id };

        db.Suppliers.AddRange(suppA, suppB);
        db.Listings.AddRange(listA, listB);
        db.Users.AddRange(customer, providerA);

        db.Bookings.AddRange(
            new Booking { Id = Guid.NewGuid(), UserId = customer.Id, ListingId = listA.Id, SupplierId = suppA.Id, StartDate = DateTime.UtcNow, Duration = "1d" },
            new Booking { Id = Guid.NewGuid(), UserId = customer.Id, ListingId = listB.Id, SupplierId = suppB.Id, StartDate = DateTime.UtcNow, Duration = "1d" });
        await db.SaveChangesAsync();

        var service = MakeService(db);

        var results = await service.GetAllAsync(providerA.Id, UserRole.Provider);

        results.Data.Should().HaveCount(1);
        results.Data.Single().Provider.Should().Be("A");
    }
}
