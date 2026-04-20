using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

public class BookingPaymentFlowTests
{
    // ─── Test infrastructure ──────────────────────────────────────────────────

    private static RuumlyDbContext CreateDb() => TestDbContext.Create();

    private static readonly PricingConfig TestPricingConfig = new(
        DefaultPartnerDiscountRate: 13m,
        DefaultVatRate:              0m,
        ExtrasMarginRate:            20m,
        RuumlyMinMarginRate:         8m,
        Starter:  new TierConfig(5m,  0m,    1,   5,   2,   false, false, false, "standard",  "email_48h"),
        Standard: new TierConfig(8m,  49m,   3,  20,  10,   false, true,  false, "boosted",   "email_24h"),
        Premium:  new TierConfig(12m, 99m,  10,  50, 999,   true,  true,  true,  "priority",  "priority_4h"));

    private static BookingService MakeBookingService(RuumlyDbContext db) =>
        new(db,
            new NoOpRouting(),
            new NoOpPricing(),
            new InvoiceService(db),   // real: exercises invoice creation in DB
            new NoOpHttp(),
            new NoOpCache(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BookingService>.Instance);

    private static OrderService MakeOrderService(RuumlyDbContext db) =>
        new(db,
            new NoOpDispatch(),
            new NoOpNotifications(),
            new InvoiceService(db),
            new NoOpEmail(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                    { ["AppUrl"] = "https://test.ruumly.eu" })
                .Build(),
            NullLogger<OrderService>.Instance,
            new NoOpHttp());

    private sealed class NoOpRouting : IOrderRoutingService
    {
        public Task RouteOrderAsync(Booking b, Listing l) => Task.CompletedTask;
    }

    private sealed class NoOpPricing : IPricingConfigService
    {
        public Task<PricingConfig> GetAsync() => Task.FromResult(TestPricingConfig);
        public Task InvalidateCacheAsync() => Task.CompletedTask;
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

    // ─── Seed helpers ─────────────────────────────────────────────────────────

    // Marketplace supplier so invoice is auto-generated; Manual integration so
    // ApproveAsync skips the dispatch service (avoids needing real HTTP clients).
    private static async Task<(Supplier supplier, Listing listing, User customer, User admin)>
        SeedAsync(RuumlyDbContext db)
    {
        var supplier = new Supplier
        {
            Id              = Guid.NewGuid(),
            Name            = "TestSupplier",
            RegistryCode    = "TST001",
            ContactName     = "Contact",
            ContactEmail    = "supplier@test.ee",
            ContactPhone    = "+372 5000 0000",
            BillingModel    = BillingModel.Marketplace,
            IntegrationType = IntegrationType.Manual,
        };

        var listing = new Listing
        {
            Id           = Guid.NewGuid(),
            SupplierId   = supplier.Id,
            Type         = ListingType.Warehouse,
            Title        = "Test Warehouse",
            Address      = "Test St 1",
            City         = "Tallinn",
            PriceFrom    = 100m,
            PriceUnit    = "kuu",
            IsActive     = true,
            AvailableNow = true,
            Description  = "Test",
            VatRate      = 0m,
        };

        var customer = new User
        {
            Id            = Guid.NewGuid(),
            Email         = "customer@test.ee",
            Name          = "Customer",
            Role          = UserRole.Customer,
            EmailVerified = true,
        };

        var admin = new User
        {
            Id    = Guid.NewGuid(),
            Email = "admin@test.ee",
            Name  = "Admin",
            Role  = UserRole.Admin,
        };

        db.Suppliers.Add(supplier);
        db.Listings.Add(listing);
        db.Users.AddRange(customer, admin);
        await db.SaveChangesAsync();

        return (supplier, listing, customer, admin);
    }

    // Seeds a minimal booking + order linked to it.
    private static async Task<(Booking booking, Order order)> SeedBookingWithOrderAsync(
        RuumlyDbContext db, Guid supplierId, Guid listingId, Guid customerId)
    {
        var booking = new Booking
        {
            Id           = Guid.NewGuid(),
            UserId       = customerId,
            ListingId    = listingId,
            SupplierId   = supplierId,
            StartDate    = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate      = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            Duration     = "1 kuu",
            Total        = 95m,
            ContactEmail = "customer@test.ee",
            ContactName  = "Customer",
            Status       = BookingStatus.Pending,
        };

        var order = new Order
        {
            Id              = Guid.NewGuid(),
            BookingId       = booking.Id,
            SupplierId      = supplierId,
            ListingId       = listingId,
            ListingTitle    = "Test Warehouse",
            ListingType     = ListingType.Warehouse,
            IntegrationType = IntegrationType.Manual,
            ApprovalMode    = ApprovalMode.Admin,
            PostingChannel  = PostingMode.Manual,
            CustomerName    = "Customer",
            CustomerEmail   = "customer@test.ee",
            CustomerPhone   = "+372 5000 0001",
            City            = "Tallinn",
            StartDate       = booking.StartDate,
            Duration        = "1 kuu",
            BasePrice       = 100m,
            PlatformPrice   = 95m,
            SupplierPrice   = 87m,
            Total           = 95m,
            Status          = OrderStatus.Created,
        };

        db.Bookings.Add(booking);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return (booking, order);
    }

    private static CreateBookingRequest MakeRequest(Guid listingId, string paymentMethod = "bank",
        string? idempotencyKey = null) => new()
    {
        ListingId      = listingId,
        StartDate      = "2026-07-01",
        EndDate        = "2026-08-01",
        Duration       = "1 kuu",
        Extras         = [],
        ContactName    = "Test Customer",
        ContactEmail   = "customer@test.ee",
        ContactPhone   = "+372 5000 0001",
        PaymentMethod  = paymentMethod,
        IdempotencyKey = idempotencyKey,
    };

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateBooking_Marketplace_Returns_InvoiceId()
    {
        var db = CreateDb();
        var (_, listing, customer, _) = await SeedAsync(db);

        var result = await MakeBookingService(db).CreateAsync(MakeRequest(listing.Id), customer.Id);

        result.InvoiceId.Should().NotBeNull("marketplace bookings auto-generate an invoice");
        var invoice = await db.Invoices.FirstAsync(i => i.BookingId == result.Id);
        invoice.Status.Should().Be(InvoiceStatus.Pending);
    }

    [Fact]
    public async Task CreateBooking_Later_Payment_InvoicePaymentMethod_CanBeSetToLater()
    {
        var db = CreateDb();
        var (_, listing, customer, _) = await SeedAsync(db);

        // Create booking — invoice is generated with no payment method yet
        var result = await MakeBookingService(db).CreateAsync(MakeRequest(listing.Id, "later"), customer.Id);
        result.InvoiceId.Should().NotBeNull();

        // Payment service would set PaymentMethod when customer selects "later"
        var invoice = await db.Invoices.FirstAsync(i => i.BookingId == result.Id);
        invoice.PaymentMethod = "later";
        await db.SaveChangesAsync();

        var fromDb = await db.Invoices.FindAsync(invoice.Id);
        fromDb!.PaymentMethod.Should().Be("later");
    }

    [Fact]
    public async Task ApproveOrder_With_PaidInvoice_Succeeds()
    {
        var db = CreateDb();
        var (supplier, listing, customer, admin) = await SeedAsync(db);
        var (booking, order) = await SeedBookingWithOrderAsync(db, supplier.Id, listing.Id, customer.Id);

        db.Invoices.Add(new Invoice
        {
            Id        = Guid.NewGuid(),
            BookingId = booking.Id,
            Amount    = 95m,
            Status    = InvoiceStatus.Paid,
            PaidAt    = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var orderService = MakeOrderService(db);
        var act = async () => await orderService.ApproveAsync(order.Id, admin.Id);

        await act.Should().NotThrowAsync();
        var updated = await db.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.Sent);
    }

    [Fact]
    public async Task ApproveOrder_With_LaterInvoice_Succeeds()
    {
        var db = CreateDb();
        var (supplier, listing, customer, admin) = await SeedAsync(db);
        var (booking, order) = await SeedBookingWithOrderAsync(db, supplier.Id, listing.Id, customer.Id);

        // "later" payment method bypasses the paid-invoice gate
        db.Invoices.Add(new Invoice
        {
            Id            = Guid.NewGuid(),
            BookingId     = booking.Id,
            Amount        = 95m,
            Status        = InvoiceStatus.Pending,
            PaymentMethod = "later",
        });
        await db.SaveChangesAsync();

        var orderService = MakeOrderService(db);
        var act = async () => await orderService.ApproveAsync(order.Id, admin.Id);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ApproveOrder_With_UnpaidBankInvoice_Throws()
    {
        var db = CreateDb();
        var (supplier, listing, customer, admin) = await SeedAsync(db);
        var (booking, order) = await SeedBookingWithOrderAsync(db, supplier.Id, listing.Id, customer.Id);

        // Pending invoice with no PaymentMethod means bank transfer not yet paid
        db.Invoices.Add(new Invoice
        {
            Id        = Guid.NewGuid(),
            BookingId = booking.Id,
            Amount    = 95m,
            Status    = InvoiceStatus.Pending,
        });
        await db.SaveChangesAsync();

        var orderService = MakeOrderService(db);
        var act = async () => await orderService.ApproveAsync(order.Id, admin.Id);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*invoice not paid*");
    }

    [Fact]
    public async Task CreateBooking_Idempotency_SecondCall_ReturnsSameBookingId()
    {
        var db  = CreateDb();
        var (_, listing, customer, _) = await SeedAsync(db);
        var key = Guid.NewGuid().ToString();

        var first  = await MakeBookingService(db).CreateAsync(MakeRequest(listing.Id, idempotencyKey: key), customer.Id);
        var second = await MakeBookingService(db).CreateAsync(MakeRequest(listing.Id, idempotencyKey: key), customer.Id);

        second.Id.Should().Be(first.Id, "duplicate key must return the original booking");
        var count = await db.Bookings.CountAsync();
        count.Should().Be(1, "only one booking should exist despite two calls");
    }
}
