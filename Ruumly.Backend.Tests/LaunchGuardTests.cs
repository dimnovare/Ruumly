using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Hangfire;
using Hangfire.InMemory;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;
using Xunit;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Focused coverage for recently-shipped launch safety guards that protect against
/// state corruption from late / out-of-order / duplicate events:
///   (1) OrderService.ConfirmAsync refuses to resurrect a terminal (Cancelled) order.
///   (2) OrderService.UpdateStatusAsync refuses to transition OUT of a terminal state.
///   (3) MontonioPaymentService 'paid' branch refuses to re-mark an already-Refunded
///       invoice Paid (acks the webhook but leaves it Refunded).
/// </summary>
[Collection(HangfireCollection.Name)]
public class LaunchGuardTests : IDisposable
{
    private const string TestSecretKey = "test-secret-key-that-is-long-enough-for-hs256";

    public LaunchGuardTests()
    {
        // MontonioPaymentService.HandleWebhookAsync may call BackgroundJob.Enqueue
        // (static). Register InMemory storage so it never throws in tests.
        JobStorage.Current = new InMemoryStorage();
    }

    public void Dispose()
    {
        // InMemory storage is GC'd — nothing persistent to clean up.
    }

    // ─── Infrastructure ────────────────────────────────────────────────────────

    private static RuumlyDbContext CreateDb() => TestDbContext.Create();

    // ── OrderService fakes (mirrors OrderServiceIdorTests) ──────────────────────

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
        public Task<InvoiceDto> MarkPaidAsync(Guid id) =>
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

    private static OrderService MakeOrderService(RuumlyDbContext db) =>
        new(db,
            new NoOpDispatch(), new NoOpNotifications(), new NoOpInvoice(),
            new NoOpEmail(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["AppUrl"] = "https://test.ruumly.eu" })
                .Build(),
            NullLogger<OrderService>.Instance,
            new NoOpHttp());

    /// <summary>
    /// Seeds a single order (with its booking) in the given status, owned by a fresh
    /// supplier. No confirmer/admin user is seeded: the status guards run regardless of
    /// who acts (a null user simply skips the Provider-ownership pre-check).
    /// </summary>
    private static async Task<Order> SeedOrderAsync(
        RuumlyDbContext db,
        OrderStatus orderStatus,
        BookingStatus bookingStatus = BookingStatus.Cancelled)
    {
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = "Sup", ContactName = "C",
            ContactEmail = "s@t.ee", ContactPhone = "1",
        };

        // Order.Booking and Booking.User are required navigations; LoadOrder Includes both.
        // Under the InMemory provider a required Include behaves like an INNER join, so a
        // missing User row would drop the whole Order — seed a matching customer.
        var customer = new User
        {
            Id = Guid.NewGuid(), Name = "Cust", Email = "c@t.ee", Role = UserRole.Customer,
        };

        var booking = new Booking
        {
            Id = Guid.NewGuid(), ListingId = Guid.NewGuid(), SupplierId = supplier.Id,
            UserId = customer.Id, ContactName = "Cust", ContactEmail = "c@t.ee",
            ContactPhone = "1", StartDate = DateTime.UtcNow, Duration = "1 kuu",
            Status = bookingStatus,
        };

        var order = new Order
        {
            Id = Guid.NewGuid(), BookingId = booking.Id, SupplierId = supplier.Id,
            ListingId = booking.ListingId, ListingTitle = "Test", ListingType = ListingType.Warehouse,
            City = "Tallinn", StartDate = DateTime.UtcNow, Duration = "1 kuu",
            CustomerName = "Cust", CustomerEmail = "c@t.ee", CustomerPhone = "1",
            IntegrationType = IntegrationType.Manual, Status = orderStatus,
        };

        db.Suppliers.Add(supplier);
        db.Users.Add(customer);
        db.Bookings.Add(booking);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    // ─── (1) ConfirmAsync: cannot resurrect a Cancelled order ───────────────────

    [Fact]
    public async Task ConfirmAsync_CancelledOrder_ThrowsArgumentException()
    {
        var db = CreateDb();
        var order = await SeedOrderAsync(db, OrderStatus.Cancelled, BookingStatus.Cancelled);
        var service = MakeOrderService(db);

        var act = async () => await service.ConfirmAsync(order.Id, Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentException>(
            "a cancelled (e.g. refunded) order must not be resurrected to Confirmed");

        // State must be untouched by the rejected confirm.
        var unchanged = await db.Orders.FindAsync(order.Id);
        unchanged!.Status.Should().Be(OrderStatus.Cancelled);
        unchanged.ConfirmedAt.Should().BeNull("a refused confirm must not stamp ConfirmedAt");
    }

    // ─── (2) UpdateStatusAsync: cannot transition OUT of a terminal state ───────

    [Theory]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Rejected)]
    public async Task UpdateStatusAsync_FromTerminalState_ThrowsArgumentException(OrderStatus terminal)
    {
        var db = CreateDb();
        var order = await SeedOrderAsync(db, terminal, BookingStatus.Cancelled);
        var service = MakeOrderService(db);

        // Attempt to re-open the closed order back to an active 'confirmed' state.
        var request = new UpdateOrderStatusRequest("confirmed", Notes: null, ApprovedBy: null);

        var act = async () => await service.UpdateStatusAsync(order.Id, request);

        await act.Should().ThrowAsync<ArgumentException>(
            $"admin override must not re-open an order out of the terminal {terminal} state");

        var unchanged = await db.Orders.FindAsync(order.Id);
        unchanged!.Status.Should().Be(terminal, "the terminal status must be preserved");
    }

    // ─── (3) Montonio 'paid' on an already-Refunded invoice: no-op (acks) ───────

    private static MontonioPaymentService MakeMontonioService(RuumlyDbContext db)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Montonio:SecretKey"]  = TestSecretKey,
                ["Montonio:AccessKey"]  = "test-access-key",
                ["Montonio:UseSandbox"] = "true",
                ["Montonio:ReturnUrl"]  = "https://ruumly.eu/pay/return",
                ["Montonio:NotifyUrl"]  = "https://ruumly.eu/api/payments/webhook",
            })
            .Build();

        return new MontonioPaymentService(
            db,
            config,
            new TestHttpClientFactory(),
            NullLogger<MontonioPaymentService>.Instance,
            new StubHttpContext());
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class StubHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    /// <summary>Builds a signature-valid Montonio webhook JWT (payload in "data" claim).</summary>
    private static string MakeWebhookJwt(string merchantReference, string paymentStatus = "paid", decimal amount = 95m)
    {
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var data  = JsonSerializer.Serialize(new
        {
            merchant_reference = merchantReference,
            payment_status     = paymentStatus,
            grand_total        = amount,
            currency           = "EUR",
        });
        var jwt = new JwtSecurityToken(
            claims: [new System.Security.Claims.Claim("data", data)],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    [Fact]
    public async Task Webhook_PaidStatus_OnRefundedInvoice_AcksButLeavesRefunded()
    {
        var db   = CreateDb();
        var ref_ = Guid.NewGuid().ToString();

        // A booking that was already voided/refunded — invoice is terminal Refunded.
        var booking = new Booking
        {
            Id = Guid.NewGuid(), ListingId = Guid.NewGuid(), SupplierId = Guid.NewGuid(),
            UserId = Guid.NewGuid(), StartDate = DateTime.UtcNow, Duration = "1 kuu",
            Status = BookingStatus.Cancelled,
        };
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(), BookingId = booking.Id, Amount = 95m,
            Status = InvoiceStatus.Refunded, PaymentOrderId = ref_,
        };
        db.Bookings.Add(booking);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var svc = MakeMontonioService(db);

        // A late 'paid' event lands for the already-refunded invoice (amount matches).
        var ok = await svc.HandleWebhookAsync(MakeWebhookJwt(ref_, paymentStatus: "paid", amount: 95m));

        ok.Should().BeTrue(
            "the webhook is acked (200) so Montonio stops retrying; reconciliation is manual");

        var unchanged = await db.Invoices.FindAsync(invoice.Id);
        unchanged!.Status.Should().Be(InvoiceStatus.Refunded,
            "a late 'paid' must NOT re-mark a refunded invoice Paid");
        unchanged.PaidAt.Should().BeNull("PaidAt must never be stamped on a refunded invoice");
    }
}
