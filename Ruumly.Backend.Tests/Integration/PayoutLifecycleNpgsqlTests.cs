using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;
using Xunit;

namespace Ruumly.Backend.Tests.Integration;

/// <summary>
/// Money-critical paths under the REAL Npgsql provider (the audit's top coverage
/// gap — InMemory can't reproduce enum string-comparison or FK enforcement).
/// Validates: PayoutStatus enum filtering translates to SQL correctly, and the
/// refund-cancels-payout fix works end-to-end against a real DB with all foreign
/// keys enforced. Isolated temp DB; per-test transaction rollback; no-ops when no
/// Postgres is reachable.
/// </summary>
[Collection("Postgres integration")]
public class PayoutLifecycleNpgsqlTests(PostgresIntegrationFixture pg)
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

    private static AdminRefundsController MakeRefunds(RuumlyDbContext db) =>
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

    // Seeds the full graph Npgsql requires (Supplier -> Listing/User -> Booking ->
    // Order -> Invoice + PayoutEntry). Returns the booking, order and payout.
    private static async Task<(Booking booking, Order order, PayoutEntry payout)> SeedAsync(
        RuumlyDbContext db, InvoiceStatus invoiceStatus, PayoutStatus payoutStatus)
    {
        var uniq = Guid.NewGuid().ToString("N")[..10];
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = "Sup", RegistryCode = uniq, ContactName = "C",
            ContactEmail = $"s{uniq}@t.ee", ContactPhone = "1", IsActive = true,
        };
        var customer = new User
        {
            Id = Guid.NewGuid(), Name = "Cust", Email = $"c{Guid.NewGuid():N}@t.ee", Role = UserRole.Customer,
        };
        var listing = new Listing
        {
            Id = Guid.NewGuid(), SupplierId = supplier.Id, Type = ListingType.Warehouse, Title = "W",
            Address = "St", City = "Tallinn", PriceFrom = 95m, PriceUnit = "kuu", IsActive = true, Description = "x",
        };
        var booking = new Booking
        {
            Id = Guid.NewGuid(), UserId = customer.Id, ListingId = listing.Id, SupplierId = supplier.Id,
            ContactName = "Cust", ContactEmail = "c@t.ee", ContactPhone = "1",
            StartDate = DateTime.UtcNow, Duration = "1 kuu", Status = BookingStatus.Confirmed, Total = 95m,
        };
        var order = new Order
        {
            Id = Guid.NewGuid(), BookingId = booking.Id, SupplierId = supplier.Id, ListingId = listing.Id,
            ListingTitle = "W", ListingType = ListingType.Warehouse, City = "Tallinn",
            StartDate = DateTime.UtcNow, Duration = "1 kuu", CustomerName = "Cust", CustomerEmail = "c@t.ee",
            CustomerPhone = "1", IntegrationType = IntegrationType.Manual, Status = OrderStatus.Confirmed,
            Total = 95m, SupplierPrice = 87m,
        };
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(), BookingId = booking.Id, Amount = 95m, Status = invoiceStatus,
        };
        var payout = new PayoutEntry
        {
            Id = Guid.NewGuid(), SupplierId = supplier.Id, OrderId = order.Id,
            SupplierAmount = 87m, PlatformMargin = 8m, Status = payoutStatus,
        };

        db.Suppliers.Add(supplier);
        db.Users.Add(customer);
        db.Listings.Add(listing);
        db.Bookings.Add(booking);
        db.Orders.Add(order);
        db.Invoices.Add(invoice);
        db.PayoutEntries.Add(payout);
        await db.SaveChangesAsync();
        return (booking, order, payout);
    }

    [Fact]
    public async Task PayoutStatus_EnumFilter_TranslatesUnderNpgsql()
    {
        if (!pg.Available) return;
        await using var db = pg.NewContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var (_, _, accrued) = await SeedAsync(db, InvoiceStatus.Pending, PayoutStatus.Accrued);
        // A second payout (Pending) on the same supplier via a fresh graph.
        await SeedAsync(db, InvoiceStatus.Pending, PayoutStatus.Pending);

        // The enum -> string SQL comparison (same class as the ListingType prod bug).
        var accruedCount = await db.PayoutEntries.CountAsync(p => p.Status == PayoutStatus.Accrued);
        var pendingCount = await db.PayoutEntries.CountAsync(p => p.Status == PayoutStatus.Pending);
        var both = await db.PayoutEntries
            .CountAsync(p => p.Status == PayoutStatus.Pending || p.Status == PayoutStatus.Accrued);

        accruedCount.Should().Be(1);
        pendingCount.Should().Be(1);
        both.Should().Be(2, "the Pending|Accrued filter used by the refund-cancel path must translate correctly");
    }

    [Fact]
    public async Task Refund_CancelsAccruedPayout_EndToEnd_UnderNpgsql()
    {
        if (!pg.Available) return;
        await using var db = pg.NewContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        // Paid invoice (refund requires it) + an Accrued payout — the double-pay case.
        var (booking, _, payout) = await SeedAsync(db, InvoiceStatus.Paid, PayoutStatus.Accrued);

        var result = await MakeRefunds(db).Refund(booking.Id, null);

        result.Should().BeOfType<OkObjectResult>();
        (await db.PayoutEntries.FindAsync(payout.Id))!.Status
            .Should().Be(PayoutStatus.Cancelled, "a refunded booking must never pay the supplier — even an Accrued payout");
        (await db.Invoices.FirstAsync(i => i.BookingId == booking.Id)).Status
            .Should().Be(InvoiceStatus.PendingRefund);
    }
}
