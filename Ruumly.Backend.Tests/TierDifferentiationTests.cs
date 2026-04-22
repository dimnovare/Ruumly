using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Http;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

public class TierDifferentiationTests
{
    // ─── Shared test infrastructure ──────────────────────────────────────────

    private static RuumlyDbContext CreateDb() => TestDbContext.Create();

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

    private static readonly PricingConfig TestPricingConfig = new(
        DefaultPartnerDiscountRate: 13m,
        DefaultVatRate:              0m,
        ExtrasMarginRate:            20m,
        RuumlyMinMarginRate:         8m,
        Starter:  new TierConfig(5m,  0m,     2,   false, false, false, "standard",  "email_48h"),
        Standard: new TierConfig(8m,  49m,  10,   false, true,  false, "boosted",   "email_24h"),
        Premium:  new TierConfig(12m, 99m, 999,   true,  true,  true,  "priority",  "priority_4h"));

    private sealed class NoOpRouting : IOrderRoutingService
    {
        public Task RouteOrderAsync(Booking b, Listing l) => Task.CompletedTask;
    }

    private sealed class NoOpPricing : IPricingConfigService
    {
        public Task<PricingConfig> GetAsync() => Task.FromResult(TestPricingConfig);
        public Task InvalidateCacheAsync() => Task.CompletedTask;
        public Task<EffectivePricing> GetEffectivePricingAsync(Supplier s) =>
            Task.FromResult(new EffectivePricing(TestPricingConfig.ForTier(s.Tier).MonthlyFee, TestPricingConfig.ForTier(s.Tier).CustomerDiscountRate));
    }

    private sealed class NoOpInvoice : IInvoiceService
    {
        public Task<List<InvoiceDto>> GetAllAsync(Guid userId, UserRole role) => Task.FromResult(new List<InvoiceDto>());
        public Task<InvoiceDto?> GetByBookingIdAsync(Guid bookingId, Guid userId, UserRole role) => Task.FromResult<InvoiceDto?>(null);
        public Task<InvoiceDto> GenerateAsync(Guid bookingId) =>
            Task.FromResult(new InvoiceDto(bookingId, bookingId, 0m, "pending", "2026-01-01", null, ""));
        public Task<InvoiceDto> MarkPaidAsync(Guid id) =>
            Task.FromResult(new InvoiceDto(id, id, 0m, "paid", "2026-01-01", "2026-01-01", ""));
    }

    private sealed class NoOpHttp : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class NoOpDispatch : IIntegrationDispatchService
    {
        public Task DispatchAsync(Order order, Supplier supplier) => Task.CompletedTask;
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

    private sealed class NoOpEmail : IEmailSender
    {
        public Task SendAsync(string to, string subject, string textBody, string? htmlBody = null)
            => Task.CompletedTask;
    }

    // ─── Seed helpers ────────────────────────────────────────────────────────

    private static Supplier MakeSupplier(
        SupplierTier tier = SupplierTier.Starter,
        bool isVerified = false,
        PriorityLevel priority = PriorityLevel.Standard) => new()
    {
        Id           = Guid.NewGuid(),
        Name         = $"Supplier-{tier}",
        ContactName  = "Test",
        ContactEmail = $"test-{Guid.NewGuid():N}@test.ee",
        ContactPhone = "+372 5000 0000",
        Tier         = tier,
        IsActive     = true,
        IsVerified   = isVerified,
        VerifiedAt   = isVerified ? DateTime.UtcNow : null,
        PriorityLevel = priority,
        Country      = "EE",
    };

    private static Listing MakeListing(Guid supplierId, decimal price = 100m) => new()
    {
        Id           = Guid.NewGuid(),
        SupplierId   = supplierId,
        Type         = ListingType.Warehouse,
        Title        = "Test Warehouse",
        Address      = "Test St 1",
        City         = "Tallinn",
        PriceFrom    = price,
        PriceUnit    = "kuu",
        IsActive     = true,
        AvailableNow = true,
        Description  = "Test",
        VatRate      = 0m,
    };

    // ─── 1. Boosted search placement ─────────────────────────────────────────

    [Fact]
    public async Task Search_DefaultSort_PremiumAppearsBeforeStarter()
    {
        var db = CreateDb();

        var starterSupplier = MakeSupplier(SupplierTier.Starter);
        var premiumSupplier = MakeSupplier(SupplierTier.Premium);
        db.Suppliers.AddRange(starterSupplier, premiumSupplier);

        var starterListing = MakeListing(starterSupplier.Id);
        var premiumListing = MakeListing(premiumSupplier.Id);
        db.Listings.AddRange(starterListing, premiumListing);
        await db.SaveChangesAsync();

        var svc = new ListingService(db, new NoOpCache());
        var result = await svc.SearchAsync(new ListingSearchRequest());

        var ids = result.Data.Select(l => l.SupplierId).ToList();
        ids.Should().ContainInOrder(
            new[] { premiumSupplier.Id, starterSupplier.Id },
            "Premium suppliers appear before Starter in default sort");
    }

    [Fact]
    public async Task Search_CheapestSort_TierBreaksTie()
    {
        var db = CreateDb();

        var starterSupplier  = MakeSupplier(SupplierTier.Starter);
        var standardSupplier = MakeSupplier(SupplierTier.Standard);
        db.Suppliers.AddRange(starterSupplier, standardSupplier);

        // Same price — tier should break the tie
        var starterListing  = MakeListing(starterSupplier.Id, 50m);
        var standardListing = MakeListing(standardSupplier.Id, 50m);
        db.Listings.AddRange(starterListing, standardListing);
        await db.SaveChangesAsync();

        var svc = new ListingService(db, new NoOpCache());
        var result = await svc.SearchAsync(new ListingSearchRequest { Sort = "cheapest" });

        var supplierIds = result.Data.Select(l => l.SupplierId).ToList();
        supplierIds.First().Should().Be(standardSupplier.Id,
            "at same price, Standard tier listing comes first");
    }

    // ─── 2. Verified badge ───────────────────────────────────────────────────

    [Fact]
    public async Task ListingDto_Shows_IsVerified_From_Supplier()
    {
        var db = CreateDb();

        var verifiedSupplier = MakeSupplier(isVerified: true);
        var unverifiedSupplier = MakeSupplier(isVerified: false);
        db.Suppliers.AddRange(verifiedSupplier, unverifiedSupplier);

        var vListing = MakeListing(verifiedSupplier.Id);
        var uListing = MakeListing(unverifiedSupplier.Id);
        db.Listings.AddRange(vListing, uListing);
        await db.SaveChangesAsync();

        var svc = new ListingService(db, new NoOpCache());
        var result = await svc.SearchAsync(new ListingSearchRequest());

        var verifiedDto = result.Data.First(l => l.SupplierId == verifiedSupplier.Id);
        var unverifiedDto = result.Data.First(l => l.SupplierId == unverifiedSupplier.Id);

        verifiedDto.IsVerified.Should().BeTrue();
        unverifiedDto.IsVerified.Should().BeFalse();
    }

    [Fact]
    public async Task Verify_Sets_IsVerified_And_VerifiedAt()
    {
        var db = CreateDb();
        var supplier = MakeSupplier();
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        supplier.IsVerified = true;
        supplier.VerifiedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var fromDb = await db.Suppliers.FindAsync(supplier.Id);
        fromDb!.IsVerified.Should().BeTrue();
        fromDb.VerifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Unverify_Clears_IsVerified_And_VerifiedAt()
    {
        var db = CreateDb();
        var supplier = MakeSupplier(isVerified: true);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        supplier.IsVerified = false;
        supplier.VerifiedAt = null;
        await db.SaveChangesAsync();

        var fromDb = await db.Suppliers.FindAsync(supplier.Id);
        fromDb!.IsVerified.Should().BeFalse();
        fromDb.VerifiedAt.Should().BeNull();
    }

    // ─── 3. Priority inbox ───────────────────────────────────────────────────

    [Fact]
    public async Task Admin_OrderList_SortsByPriorityLevel_Desc()
    {
        var db = CreateDb();

        var normalSupplier   = MakeSupplier(priority: PriorityLevel.Standard);
        var criticalSupplier = MakeSupplier(priority: PriorityLevel.Critical);
        db.Suppliers.AddRange(normalSupplier, criticalSupplier);

        var customer = new User
        {
            Id = Guid.NewGuid(), Email = "c@test.ee", Name = "C",
            Role = UserRole.Customer, EmailVerified = true,
        };
        db.Users.Add(customer);

        // Two bookings so orders have the required FK
        var booking1 = new Booking
        {
            Id = Guid.NewGuid(), UserId = customer.Id,
            ListingId = Guid.NewGuid(), SupplierId = normalSupplier.Id,
            StartDate = DateTime.UtcNow, EndDate = DateTime.UtcNow.AddDays(30),
            Duration = "1 kuu", Status = BookingStatus.Pending,
        };
        var booking2 = new Booking
        {
            Id = Guid.NewGuid(), UserId = customer.Id,
            ListingId = Guid.NewGuid(), SupplierId = criticalSupplier.Id,
            StartDate = DateTime.UtcNow, EndDate = DateTime.UtcNow.AddDays(30),
            Duration = "1 kuu", Status = BookingStatus.Pending,
        };
        db.Bookings.AddRange(booking1, booking2);

        var order1 = new Order
        {
            Id = Guid.NewGuid(), BookingId = booking1.Id,
            SupplierId = normalSupplier.Id, ListingId = Guid.NewGuid(),
            ListingTitle = "W1", ListingType = ListingType.Warehouse,
            IntegrationType = IntegrationType.Manual, ApprovalMode = ApprovalMode.Admin,
            PostingChannel = PostingMode.Manual,
            CustomerName = "C", CustomerEmail = "c@test.ee", CustomerPhone = "+372 5",
            City = "Tallinn", StartDate = DateTime.UtcNow, Duration = "1 kuu",
            BasePrice = 100m, PlatformPrice = 95m, SupplierPrice = 87m, Total = 95m,
            Status = OrderStatus.Created,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
        };
        var order2 = new Order
        {
            Id = Guid.NewGuid(), BookingId = booking2.Id,
            SupplierId = criticalSupplier.Id, ListingId = Guid.NewGuid(),
            ListingTitle = "W2", ListingType = ListingType.Warehouse,
            IntegrationType = IntegrationType.Manual, ApprovalMode = ApprovalMode.Admin,
            PostingChannel = PostingMode.Manual,
            CustomerName = "C", CustomerEmail = "c@test.ee", CustomerPhone = "+372 5",
            City = "Tallinn", StartDate = DateTime.UtcNow, Duration = "1 kuu",
            BasePrice = 100m, PlatformPrice = 95m, SupplierPrice = 87m, Total = 95m,
            Status = OrderStatus.Created,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5), // newer but should still sort by priority
        };
        db.Orders.AddRange(order1, order2);
        await db.SaveChangesAsync();

        var admin = new User { Id = Guid.NewGuid(), Email = "a@test.ee", Name = "Admin", Role = UserRole.Admin };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var orderService = new OrderService(db,
            new NoOpDispatch(), new NoOpNotifications(), new NoOpInvoice(),
            new NoOpEmail(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["AppUrl"] = "https://test.ruumly.eu" })
                .Build(),
            NullLogger<OrderService>.Instance,
            new NoOpHttp());

        var result = await orderService.GetAllAsync(admin.Id, UserRole.Admin);

        result.Data.First().SupplierId.Should().Be(criticalSupplier.Id,
            "Critical priority orders appear first in admin inbox");
    }

    // ─── 4. Featured partner rotation ────────────────────────────────────────

    [Fact]
    public async Task FeaturedPartners_Returns_Only_Verified_StandardOrAbove()
    {
        var db = CreateDb();

        var starterVerified  = MakeSupplier(SupplierTier.Starter, isVerified: true);
        var standardVerified = MakeSupplier(SupplierTier.Standard, isVerified: true);
        var premiumVerified  = MakeSupplier(SupplierTier.Premium, isVerified: true);
        var premiumNotVerified = MakeSupplier(SupplierTier.Premium, isVerified: false);

        db.Suppliers.AddRange(starterVerified, standardVerified, premiumVerified, premiumNotVerified);

        // Add active listings so count is populated
        db.Listings.AddRange(
            MakeListing(standardVerified.Id),
            MakeListing(premiumVerified.Id),
            MakeListing(premiumVerified.Id));
        await db.SaveChangesAsync();

        var svc = new FeaturedPartnersService(db, new NoOpCache());
        var result = await svc.GetFeaturedAsync();

        result.Should().HaveCount(2, "only Standard+verified and Premium+verified qualify");
        result.Should().Contain(p => p.Id == standardVerified.Id);
        result.Should().Contain(p => p.Id == premiumVerified.Id);
        result.Should().NotContain(p => p.Id == starterVerified.Id, "Starter is excluded");
        result.Should().NotContain(p => p.Id == premiumNotVerified.Id, "unverified is excluded");
    }

    [Fact]
    public async Task FeaturedPartners_Includes_ListingCount()
    {
        var db = CreateDb();
        var supplier = MakeSupplier(SupplierTier.Premium, isVerified: true);
        db.Suppliers.Add(supplier);
        db.Listings.AddRange(
            MakeListing(supplier.Id),
            MakeListing(supplier.Id),
            MakeListing(supplier.Id));
        await db.SaveChangesAsync();

        var svc = new FeaturedPartnersService(db, new NoOpCache());
        var result = await svc.GetFeaturedAsync();

        result.Should().ContainSingle();
        result.First().ListingCount.Should().Be(3);
    }

    // ─── 5. Analytics gating — no backend gate ───────────────────────────────

    [Fact]
    public void TierRules_HasFullAnalytics_StarterFalse_StandardAndAboveTrue()
    {
        Ruumly.Backend.Helpers.TierRules.HasFullAnalytics(SupplierTier.Starter).Should().BeFalse();
        Ruumly.Backend.Helpers.TierRules.HasFullAnalytics(SupplierTier.Standard).Should().BeTrue();
        Ruumly.Backend.Helpers.TierRules.HasFullAnalytics(SupplierTier.Premium).Should().BeTrue();
    }
}
