using FluentAssertions;
using Hangfire;
using Hangfire.InMemory;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Validates that terminal booking/order states are not corrupted by
/// late or out-of-order webhook/admin events.
/// </summary>
[Collection(HangfireCollection.Name)]
public class BookingStateMachineTests : IDisposable
{
    // ─── Hangfire in-memory setup ─────────────────────────────────────────────

    public BookingStateMachineTests()
    {
        JobStorage.Current = new InMemoryStorage();
    }

    public void Dispose() { }

    // ─── Infrastructure ───────────────────────────────────────────────────────

    private static RuumlyDbContext CreateDb() => TestDbContext.Create();

    // ─── Seed ─────────────────────────────────────────────────────────────────

    private static async Task<(Booking booking, Invoice invoice, Order order)> SeedAsync(
        RuumlyDbContext db,
        BookingStatus bookingStatus = BookingStatus.Pending,
        InvoiceStatus invoiceStatus = InvoiceStatus.AwaitingPayment,
        OrderStatus   orderStatus   = OrderStatus.Created,
        bool          autoDispatch  = true)
    {
        var supplier = new Supplier
        {
            Id              = Guid.NewGuid(),
            Name            = "TestSupplier",
            RegistryCode    = "TST001",
            ContactName     = "Contact",
            ContactEmail    = "s@test.ee",
            ContactPhone    = "+372 5000 0000",
            IntegrationType = IntegrationType.Manual,
        };

        var listing = new Listing
        {
            Id          = Guid.NewGuid(),
            SupplierId  = supplier.Id,
            Type        = ListingType.Warehouse,
            Title       = "Test Warehouse",
            Address     = "Test St 1",
            City        = "Tallinn",
            PriceUnit   = "kuu",
            Description = "Test",
        };

        var customer = new User
        {
            Id            = Guid.NewGuid(),
            Email         = "customer@test.ee",
            Name          = "Customer",
            Role          = UserRole.Customer,
            EmailVerified = true,
        };

        var booking = new Booking
        {
            Id         = Guid.NewGuid(),
            UserId     = customer.Id,
            ListingId  = listing.Id,
            SupplierId = supplier.Id,
            StartDate  = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate    = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            Duration   = "1 kuu",
            Status     = bookingStatus,
            Total      = 95m,
            ContactName  = "Customer",
            ContactEmail = "customer@test.ee",
            ContactPhone = "+372 5000 0001",
        };

        var merchantRef = Guid.NewGuid().ToString();
        var invoice = new Invoice
        {
            Id             = Guid.NewGuid(),
            BookingId      = booking.Id,
            Amount         = 95m,
            Status         = invoiceStatus,
            PaymentOrderId = merchantRef,
        };

        var order = new Order
        {
            Id              = Guid.NewGuid(),
            BookingId       = booking.Id,
            SupplierId      = supplier.Id,
            ListingId       = listing.Id,
            ListingTitle    = "Test Warehouse",
            ListingType     = ListingType.Warehouse,
            IntegrationType = IntegrationType.Manual,
            ApprovalMode    = ApprovalMode.Auto,
            PostingChannel  = PostingMode.Manual,
            AutoDispatch    = autoDispatch,
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
            Status          = orderStatus,
        };

        db.Suppliers.Add(supplier);
        db.Listings.Add(listing);
        db.Users.Add(customer);
        db.Bookings.Add(booking);
        db.Invoices.Add(invoice);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        return (booking, invoice, order);
    }

    // ─── Tests ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cancelling a booking that is already Cancelled must be rejected — double-cancel
    /// must not corrupt the booking row or create duplicate timeline entries.
    /// </summary>
    [Fact]
    public async Task Cancel_AlreadyCancelled_Throws()
    {
        var db = CreateDb();
        var (booking, _, _) = await SeedAsync(db, bookingStatus: BookingStatus.Cancelled);

        var svc = new BookingService(
            db,
            new StubRouting(),
            new StubPricing(),
            new StubInvoice(),
            new StubHttp(),
            new StubCache(),
            NullLogger<BookingService>.Instance);

        var act = async () => await svc.CancelAsync(booking.Id, booking.UserId, UserRole.Customer);

        await act.Should().ThrowAsync<ArgumentException>(
            "cancelling an already-cancelled booking must fail");
    }

    /// <summary>
    /// Cancelling a Completed booking must be rejected — completed is terminal.
    /// </summary>
    [Fact]
    public async Task Cancel_CompletedBooking_Throws()
    {
        var db = CreateDb();
        var (booking, _, _) = await SeedAsync(db, bookingStatus: BookingStatus.Completed);

        var svc = new BookingService(
            db,
            new StubRouting(),
            new StubPricing(),
            new StubInvoice(),
            new StubHttp(),
            new StubCache(),
            NullLogger<BookingService>.Instance);

        var act = async () => await svc.CancelAsync(booking.Id, booking.UserId, UserRole.Customer);

        await act.Should().ThrowAsync<ArgumentException>(
            "cancelling a completed booking must fail");
    }

    /// <summary>
    /// Cancelling a booking whose invoice was already Paid transitions the invoice
    /// to PendingRefund rather than voiding it, so the money can be manually refunded.
    /// </summary>
    [Fact]
    public async Task Cancel_PaidInvoice_TransitionsTo_PendingRefund()
    {
        var db = CreateDb();
        var (booking, invoice, _) = await SeedAsync(
            db,
            bookingStatus: BookingStatus.Pending,
            invoiceStatus: InvoiceStatus.Paid);

        var svc = new BookingService(
            db,
            new StubRouting(),
            new StubPricing(),
            new StubInvoice(),
            new StubHttp(),
            new StubCache(),
            NullLogger<BookingService>.Instance);

        await svc.CancelAsync(booking.Id, booking.UserId, UserRole.Customer);

        var updatedInvoice = await db.Invoices.FindAsync(invoice.Id);
        updatedInvoice!.Status.Should().Be(InvoiceStatus.PendingRefund,
            "a paid invoice on a cancelled booking must require a manual refund");
    }

    /// <summary>
    /// Cancelling a booking with a Pending (unpaid) invoice should void it immediately
    /// rather than flagging for refund (there's nothing to refund).
    /// </summary>
    [Fact]
    public async Task Cancel_PendingInvoice_TransitionsTo_Refunded_Immediately()
    {
        var db = CreateDb();
        var (booking, invoice, _) = await SeedAsync(
            db,
            bookingStatus: BookingStatus.Pending,
            invoiceStatus: InvoiceStatus.Pending);

        var svc = new BookingService(
            db,
            new StubRouting(),
            new StubPricing(),
            new StubInvoice(),
            new StubHttp(),
            new StubCache(),
            NullLogger<BookingService>.Instance);

        await svc.CancelAsync(booking.Id, booking.UserId, UserRole.Customer);

        var updatedInvoice = await db.Invoices.FindAsync(invoice.Id);
        updatedInvoice!.Status.Should().Be(InvoiceStatus.Refunded,
            "a pending invoice on a cancelled booking should be voided immediately");
    }

    /// <summary>
    /// Cancelling a booking cancels the linked order too, and nullifies a pending payout.
    /// </summary>
    [Fact]
    public async Task Cancel_Booking_Also_Cancels_Order_And_Payout()
    {
        var db = CreateDb();
        var (booking, _, order) = await SeedAsync(
            db,
            bookingStatus: BookingStatus.Pending,
            invoiceStatus: InvoiceStatus.Pending,
            orderStatus:   OrderStatus.Created);

        // Add a pending payout entry for the order
        var payout = new PayoutEntry
        {
            Id             = Guid.NewGuid(),
            OrderId        = order.Id,
            SupplierId     = order.SupplierId,
            SupplierAmount = 87m,
            PlatformMargin = 8m,
            Status         = PayoutStatus.Pending,
            CreatedAt      = DateTime.UtcNow,
        };
        db.PayoutEntries.Add(payout);
        await db.SaveChangesAsync();

        var svc = new BookingService(
            db,
            new StubRouting(),
            new StubPricing(),
            new StubInvoice(),
            new StubHttp(),
            new StubCache(),
            NullLogger<BookingService>.Instance);

        await svc.CancelAsync(booking.Id, booking.UserId, UserRole.Customer);

        var updatedOrder = await db.Orders.FindAsync(order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.Cancelled,
            "order must be cancelled when booking is cancelled");

        var updatedPayout = await db.PayoutEntries.FindAsync(payout.Id);
        updatedPayout!.Status.Should().Be(PayoutStatus.Cancelled,
            "pending payout must be cancelled so the supplier is not paid for a cancelled order");
    }

    // ─── Stubs ────────────────────────────────────────────────────────────────

    private sealed class StubRouting : IOrderRoutingService
    {
        public Task RouteOrderAsync(Booking b, Listing l) => Task.CompletedTask;
    }

    private sealed class StubPricing : IPricingConfigService
    {
        private static readonly PricingConfig Cfg = new(13m, 0m, 20m, 8m,
            new TierConfig(5m,  0m,  12m,  2,  false, false, false, "standard", "email_48h"),
            new TierConfig(8m,  49m,  8m, 10,  false, true,  false, "boosted",  "email_24h"),
            new TierConfig(12m, 99m,  6m, 999, true,  true,  true,  "priority", "priority_4h"));
        public Task<PricingConfig> GetAsync() => Task.FromResult(Cfg);
        public Task InvalidateCacheAsync() => Task.CompletedTask;
        public Task<EffectivePricing> GetEffectivePricingAsync(Supplier s) =>
            Task.FromResult(new EffectivePricing(0m, 0m));
    }

    private sealed class StubInvoice : IInvoiceService
    {
        public Task<PaginatedResult<InvoiceDto>> GetAllAsync(Guid uid, UserRole role, int page = 1, int limit = 100) =>
            Task.FromResult(new PaginatedResult<InvoiceDto>([], 0, page, limit, false));
        public Task<InvoiceDto?> GetByBookingIdAsync(Guid bid, Guid uid, UserRole role) =>
            Task.FromResult<InvoiceDto?>(null);
        public Task<InvoiceDto> GenerateAsync(Guid bid, string? paymentMethod = null) =>
            Task.FromResult(new InvoiceDto(bid, bid, 0m, "pending", "2026-01-01", null, ""));
        public Task<InvoiceDto> MarkPaidAsync(Guid id, string? paymentReference = null) =>
            Task.FromResult(new InvoiceDto(id, id, 0m, "paid", "2026-01-01", "2026-01-01", ""));
    }

    private sealed class StubHttp : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
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
}
