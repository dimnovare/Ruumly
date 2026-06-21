using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
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
/// GetStatusCountsAsync (admin status tabs) and the GetAllAsync server-side
/// status filter that backs AdminOrders pagination.
/// </summary>
public class OrderStatusCountsTests
{
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

    private sealed class NoOpInvoice : IInvoiceService
    {
        public Task<PaginatedResult<InvoiceDto>> GetAllAsync(Guid userId, UserRole role, int page = 1, int limit = 100) => Task.FromResult(new PaginatedResult<InvoiceDto>(new List<InvoiceDto>(), 0, page, limit, false));
        public Task<InvoiceDto?> GetByBookingIdAsync(Guid bookingId, Guid userId, UserRole role) => Task.FromResult<InvoiceDto?>(null);
        public Task<InvoiceDto> GenerateAsync(Guid bookingId, string? paymentMethod = null) =>
            Task.FromResult(new InvoiceDto(bookingId, bookingId, 0m, "pending", "2026-01-01", null, ""));
        public Task<InvoiceDto> MarkPaidAsync(Guid id, string? paymentReference = null) =>
            Task.FromResult(new InvoiceDto(id, id, 0m, "paid", "2026-01-01", "2026-01-01", ""));
    }

    private sealed class NoOpEmail : IEmailSender
    {
        public Task SendAsync(string to, string subject, string textBody, string? htmlBody = null)
            => Task.CompletedTask;
    }

    private sealed class NoOpHttp : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private static OrderService MakeService(RuumlyDbContext db) =>
        new(db,
            new NoOpDispatch(), new NoOpNotifications(), new NoOpInvoice(),
            new NoOpEmail(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["AppUrl"] = "https://test.ruumly.eu" })
                .Build(),
            NullLogger<OrderService>.Instance,
            new NoOpHttp());

    private static void AddOrder(RuumlyDbContext db, Guid supplierId, OrderStatus status)
    {
        var bookingId = Guid.NewGuid();
        var listingId = Guid.NewGuid();
        db.Bookings.Add(new Booking
        {
            Id = bookingId, ListingId = listingId, SupplierId = supplierId,
            UserId = Guid.NewGuid(), ContactName = "C", ContactEmail = "c@t.ee",
            ContactPhone = "1", StartDate = DateTime.UtcNow, Status = BookingStatus.Confirmed,
        });
        db.Orders.Add(new Order
        {
            Id = Guid.NewGuid(), BookingId = bookingId, SupplierId = supplierId,
            ListingId = listingId, ListingTitle = "T", ListingType = ListingType.Warehouse,
            City = "Tallinn", StartDate = DateTime.UtcNow, Duration = "1 kuu",
            CustomerName = "C", CustomerEmail = "c@t.ee", CustomerPhone = "1",
            IntegrationType = IntegrationType.Manual, Status = status,
        });
    }

    /// <summary>supplierA: 2×Created, 1×Sent, 1×Confirmed. supplierB: 1×Created, 1×Completed.</summary>
    private static async Task<(Guid supplierA, Guid supplierB)> SeedAsync(RuumlyDbContext db)
    {
        var supplierA = new Supplier { Id = Guid.NewGuid(), Name = "SupA", ContactName = "A", ContactEmail = "a@t.ee", ContactPhone = "1" };
        var supplierB = new Supplier { Id = Guid.NewGuid(), Name = "SupB", ContactName = "B", ContactEmail = "b@t.ee", ContactPhone = "2" };
        db.Suppliers.AddRange(supplierA, supplierB);

        AddOrder(db, supplierA.Id, OrderStatus.Created);
        AddOrder(db, supplierA.Id, OrderStatus.Created);
        AddOrder(db, supplierA.Id, OrderStatus.Sent);
        AddOrder(db, supplierA.Id, OrderStatus.Confirmed);
        AddOrder(db, supplierB.Id, OrderStatus.Created);
        AddOrder(db, supplierB.Id, OrderStatus.Completed);

        await db.SaveChangesAsync();
        return (supplierA.Id, supplierB.Id);
    }

    [Fact]
    public async Task StatusCounts_Admin_GroupsAllOrdersWithTotal()
    {
        var db = TestDbContext.Create();
        await SeedAsync(db);
        var service = MakeService(db);

        var counts = await service.GetStatusCountsAsync(Guid.NewGuid(), UserRole.Admin);

        counts["created"].Should().Be(3);
        counts["sent"].Should().Be(1);
        counts["confirmed"].Should().Be(1);
        counts["completed"].Should().Be(1);
        counts["all"].Should().Be(6);
        counts.ContainsKey("rejected").Should().BeFalse("statuses with no orders are omitted");
    }

    [Fact]
    public async Task StatusCounts_Admin_ScopedToSupplier()
    {
        var db = TestDbContext.Create();
        var (supplierA, _) = await SeedAsync(db);
        var service = MakeService(db);

        var counts = await service.GetStatusCountsAsync(Guid.NewGuid(), UserRole.Admin, supplierA);

        counts["created"].Should().Be(2);
        counts["sent"].Should().Be(1);
        counts["confirmed"].Should().Be(1);
        counts["all"].Should().Be(4);
        counts.ContainsKey("completed").Should().BeFalse("the completed order belongs to supplier B");
    }

    [Fact]
    public async Task GetAll_StatusFilter_ReturnsOnlyMatchingStatus()
    {
        var db = TestDbContext.Create();
        await SeedAsync(db);
        var service = MakeService(db);

        var created = await service.GetAllAsync(Guid.NewGuid(), UserRole.Admin, status: "created");
        created.Total.Should().Be(3);
        created.Data.Should().OnlyContain(o => o.Status == "created");

        var sent = await service.GetAllAsync(Guid.NewGuid(), UserRole.Admin, status: "sent");
        sent.Total.Should().Be(1);
    }

    [Fact]
    public async Task GetAll_NoStatusFilter_ReturnsEverything()
    {
        var db = TestDbContext.Create();
        await SeedAsync(db);
        var service = MakeService(db);

        var all = await service.GetAllAsync(Guid.NewGuid(), UserRole.Admin);

        all.Total.Should().Be(6);
    }
}
