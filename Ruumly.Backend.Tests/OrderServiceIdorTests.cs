using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// IDOR guard for OrderService.GetByIdAsync(id, callerId, callerRole, ct).
/// Mirrors GetByBookingIdAsync ownership checks: a Provider may only read their
/// own supplier's order; the owning Provider and any Admin succeed.
/// </summary>
public class OrderServiceIdorTests
{
    // ─── Infrastructure ────────────────────────────────────────────────────

    private static RuumlyDbContext CreateDb() => TestDbContext.Create();

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

    // ─── Seed helper ───────────────────────────────────────────────────────

    /// <summary>
    /// Seeds an order owned by supplierA, plus a customer, the owning provider
    /// (providerA on supplierA), and a foreign provider (providerB on supplierB).
    /// </summary>
    private static async Task<(Guid orderId, Guid customerId, Guid ownerProviderId, Guid foreignProviderId)>
        SeedOrderAsync(RuumlyDbContext db)
    {
        var supplierA = new Supplier { Id = Guid.NewGuid(), Name = "SupA", ContactName = "A", ContactEmail = "a@t.ee", ContactPhone = "1" };
        var supplierB = new Supplier { Id = Guid.NewGuid(), Name = "SupB", ContactName = "B", ContactEmail = "b@t.ee", ContactPhone = "2" };

        var customer        = new User { Id = Guid.NewGuid(), Name = "Cust", Email = "c@t.ee", Role = UserRole.Customer };
        var ownerProvider   = new User { Id = Guid.NewGuid(), Name = "POwn", Email = "pa@t.ee", Role = UserRole.Provider, SupplierId = supplierA.Id };
        var foreignProvider = new User { Id = Guid.NewGuid(), Name = "PFor", Email = "pb@t.ee", Role = UserRole.Provider, SupplierId = supplierB.Id };

        var bookingId = Guid.NewGuid();
        var listingId = Guid.NewGuid();
        var orderId   = Guid.NewGuid();

        var booking = new Booking
        {
            Id = bookingId, ListingId = listingId, SupplierId = supplierA.Id,
            UserId = customer.Id, ContactName = "Cust", ContactEmail = "c@t.ee",
            ContactPhone = "1", StartDate = DateTime.UtcNow, Status = BookingStatus.Confirmed,
        };

        var order = new Order
        {
            Id = orderId, BookingId = bookingId, SupplierId = supplierA.Id,
            ListingId = listingId, ListingTitle = "Test", ListingType = ListingType.Warehouse,
            City = "Tallinn", StartDate = DateTime.UtcNow, Duration = "1 kuu",
            CustomerName = "Cust", CustomerEmail = "c@t.ee", CustomerPhone = "1",
            IntegrationType = IntegrationType.Manual, Status = OrderStatus.Created,
        };

        db.Suppliers.AddRange(supplierA, supplierB);
        db.Users.AddRange(customer, ownerProvider, foreignProvider);
        db.Bookings.Add(booking);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        return (orderId, customer.Id, ownerProvider.Id, foreignProvider.Id);
    }

    // ─── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ForeignProvider_Throws_Forbidden()
    {
        var db = CreateDb();
        var (orderId, _, _, foreignProviderId) = await SeedOrderAsync(db);
        var service = MakeService(db);

        var act = async () => await service.GetByIdAsync(orderId, foreignProviderId, UserRole.Provider);

        await act.Should().ThrowAsync<ForbiddenException>(
            "a provider on a different supplier must not read another supplier's order");
    }

    [Fact]
    public async Task GetByIdAsync_OwningProvider_ReturnsOrder()
    {
        var db = CreateDb();
        var (orderId, _, ownerProviderId, _) = await SeedOrderAsync(db);
        var service = MakeService(db);

        var result = await service.GetByIdAsync(orderId, ownerProviderId, UserRole.Provider);

        result.Should().NotBeNull();
        result!.Id.Should().Be(orderId);
    }

    [Fact]
    public async Task GetByIdAsync_Admin_ReturnsOrder()
    {
        var db = CreateDb();
        var (orderId, _, _, _) = await SeedOrderAsync(db);
        var service = MakeService(db);

        // Admin always passes the IDOR guard — callerId is irrelevant for Admin.
        var result = await service.GetByIdAsync(orderId, Guid.NewGuid(), UserRole.Admin);

        result.Should().NotBeNull();
        result!.Id.Should().Be(orderId);
    }
}
