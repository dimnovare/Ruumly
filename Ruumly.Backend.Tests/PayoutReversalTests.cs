using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Jobs;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// The supplier ledger invariant: whenever a booking is cancelled/refunded, its
/// pending PayoutEntry must be Cancelled so the supplier is never paid for a
/// booking the customer didn't keep. Covers the stale-cleanup job and the admin
/// refund endpoints (the Montonio refund-webhook path is covered in
/// MontonioWebhookIdempotencyTests).
/// </summary>
public class PayoutReversalTests
{
    private sealed class NoOpNotifications : INotificationService
    {
        public Task<PaginatedResult<NotificationDto>> GetAllAsync(Guid userId, int page = 1, int limit = 50)
            => Task.FromResult(new PaginatedResult<NotificationDto>([], 0, page, limit, false));
        public Task MarkReadAsync(Guid id, Guid userId) => Task.CompletedTask;
        public Task MarkAllReadAsync(Guid userId) => Task.CompletedTask;
        public Task CreateAsync(Guid userId, NotificationType type, string title, string desc,
            string? actionUrl = null, string? entityId = null, string? entityType = null) => Task.CompletedTask;
    }

    private sealed class StubHttp : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } }

    private static AdminRefundsController MakeController(RuumlyDbContext db) =>
        new(db, new StubHttp(), new NoOpNotifications())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], "test")),
                },
            },
        };

    private static (Booking booking, Order order, PayoutEntry payout) Seed(
        RuumlyDbContext db, BookingStatus bookingStatus, OrderStatus orderStatus,
        InvoiceStatus? invoiceStatus, DateTime createdAt)
    {
        var supplier = new Supplier { Id = Guid.NewGuid(), Name = "S", ContactName = "C", ContactEmail = "s@t.ee", ContactPhone = "1" };
        var customer = new User { Id = Guid.NewGuid(), Name = "C", Email = "c@t.ee", Role = UserRole.Customer };
        var listingId = Guid.NewGuid();

        var booking = new Booking
        {
            Id = Guid.NewGuid(), UserId = customer.Id, ListingId = listingId, SupplierId = supplier.Id,
            ContactName = "C", ContactEmail = "c@t.ee", ContactPhone = "1",
            StartDate = DateTime.UtcNow, Status = bookingStatus, CreatedAt = createdAt, Total = 95m,
        };
        var order = new Order
        {
            Id = Guid.NewGuid(), BookingId = booking.Id, SupplierId = supplier.Id, ListingId = listingId,
            ListingTitle = "T", ListingType = ListingType.Warehouse, City = "Tallinn",
            StartDate = DateTime.UtcNow, Duration = "1 kuu", CustomerName = "C", CustomerEmail = "c@t.ee",
            CustomerPhone = "1", IntegrationType = IntegrationType.Manual, Status = orderStatus,
            Total = 95m, SupplierPrice = 87m,
        };
        var payout = new PayoutEntry
        {
            Id = Guid.NewGuid(), SupplierId = supplier.Id, OrderId = order.Id,
            SupplierAmount = 87m, PlatformMargin = 8m, Status = PayoutStatus.Pending,
        };

        db.Suppliers.Add(supplier);
        db.Users.Add(customer);
        db.Bookings.Add(booking);
        db.Orders.Add(order);
        db.PayoutEntries.Add(payout);
        if (invoiceStatus is not null)
            db.Invoices.Add(new Invoice { Id = Guid.NewGuid(), BookingId = booking.Id, Amount = 95m, Status = invoiceStatus.Value });
        db.SaveChanges();

        return (booking, order, payout);
    }

    [Fact]
    public async Task StaleCleanup_CancelsPendingPayout()
    {
        var db = TestDbContext.Create();
        var (booking, order, payout) = Seed(
            db, BookingStatus.Pending, OrderStatus.Created,
            invoiceStatus: null, createdAt: DateTime.UtcNow.AddHours(-48));

        await new StaleBookingCleanupJob(db, NullLogger<StaleBookingCleanupJob>.Instance).ExecuteAsync();

        (await db.Bookings.FindAsync(booking.Id))!.Status.Should().Be(BookingStatus.Cancelled);
        (await db.Orders.FindAsync(order.Id))!.Status.Should().Be(OrderStatus.Cancelled);
        (await db.PayoutEntries.FindAsync(payout.Id))!.Status
            .Should().Be(PayoutStatus.Cancelled, "a stale unpaid booking must not leave the supplier owed a payout");
    }

    [Fact]
    public async Task MarkRefunded_CancelsPendingPayout()
    {
        var db = TestDbContext.Create();
        var (booking, _, payout) = Seed(
            db, BookingStatus.Cancelled, OrderStatus.Cancelled,
            invoiceStatus: InvoiceStatus.PendingRefund, createdAt: DateTime.UtcNow);
        var invoice = await db.Invoices.FirstAsync(i => i.BookingId == booking.Id);

        var result = await MakeController(db).MarkRefunded(invoice.Id);

        result.Should().BeOfType<OkObjectResult>();
        (await db.Invoices.FindAsync(invoice.Id))!.Status.Should().Be(InvoiceStatus.Refunded);
        (await db.PayoutEntries.FindAsync(payout.Id))!.Status.Should().Be(PayoutStatus.Cancelled);
    }

    [Fact]
    public async Task CancelAndRefund_PaidInvoice_CancelsPayout_MarksPendingRefund_AndCancelsBooking()
    {
        var db = TestDbContext.Create();
        var (booking, order, payout) = Seed(
            db, BookingStatus.Confirmed, OrderStatus.Confirmed,
            invoiceStatus: InvoiceStatus.Paid, createdAt: DateTime.UtcNow);

        var result = await MakeController(db).CancelAndRefund(order.Id);

        result.Should().BeOfType<OkObjectResult>();
        (await db.Orders.FindAsync(order.Id))!.Status.Should().Be(OrderStatus.Cancelled);
        (await db.Bookings.FindAsync(booking.Id))!.Status.Should().Be(BookingStatus.Cancelled);
        (await db.PayoutEntries.FindAsync(payout.Id))!.Status.Should().Be(PayoutStatus.Cancelled);
        (await db.Invoices.FirstAsync(i => i.BookingId == booking.Id)).Status
            .Should().Be(InvoiceStatus.PendingRefund, "a paid invoice needs a manual bank refund, not an instant void");
    }
}
