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
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Validates that the Montonio webhook handler is idempotent:
/// firing the same webhook twice must not charge twice, not corrupt state,
/// and must return true (success) on the duplicate call.
/// </summary>
public class MontonioWebhookIdempotencyTests : IDisposable
{
    private const string TestSecretKey = "test-secret-key-that-is-long-enough-for-hs256";

    // ─── Hangfire in-memory storage ───────────────────────────────────────────
    // BackgroundJob.Enqueue is a static call inside HandleWebhookAsync.
    // Without a registered JobStorage it throws. We use the InMemory storage
    // to keep tests self-contained.

    public MontonioWebhookIdempotencyTests()
    {
        JobStorage.Current = new InMemoryStorage();
    }

    public void Dispose()
    {
        // Nothing persistent to clean up — InMemory storage is GC'd.
    }

    // ─── Infrastructure ───────────────────────────────────────────────────────

    private static RuumlyDbContext CreateDb() => TestDbContext.Create();

    private static MontonioPaymentService MakeService(RuumlyDbContext db)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Montonio:SecretKey"]    = TestSecretKey,
                ["Montonio:AccessKey"]    = "test-access-key",
                ["Montonio:UseSandbox"]   = "true",
                ["Montonio:ReturnUrl"]    = "https://ruumly.eu/pay/return",
                ["Montonio:NotifyUrl"]    = "https://ruumly.eu/api/payments/webhook",
            })
            .Build();

        var httpFactory = new TestHttpClientFactory();

        return new MontonioPaymentService(
            db,
            config,
            httpFactory,
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

    // ─── JWT helper ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a Montonio-style webhook JWT (data claim wraps the payload JSON).
    /// <paramref name="amount"/> defaults to 95m to match the seeded invoice Amount;
    /// override it to simulate an amount-mismatched event.
    /// </summary>
    private static string MakeWebhookJwt(string merchantReference, string paymentStatus = "paid", decimal amount = 95m)
    {
        var key    = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecretKey));
        var creds  = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var data   = JsonSerializer.Serialize(new
        {
            merchant_reference = merchantReference,
            payment_status     = paymentStatus,
            grand_total        = amount,   // defaults to the seeded invoice Amount (95m)
            currency           = "EUR",
        });
        var jwt = new JwtSecurityToken(
            claims: [new System.Security.Claims.Claim("data", data)],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    // ─── Seed helpers ─────────────────────────────────────────────────────────

    private static async Task<(Invoice invoice, Order order)> SeedPaidScenarioAsync(
        RuumlyDbContext db,
        string merchantRef,
        InvoiceStatus invoiceStatus = InvoiceStatus.AwaitingPayment,
        OrderStatus orderStatus     = OrderStatus.Created)
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
            Status     = BookingStatus.Pending,
            Total      = 95m,
        };

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
            AutoDispatch    = true,
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

        return (invoice, order);
    }

    // ─── Tests ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Happy path: webhook marks invoice as Paid and enqueues dispatch.
    /// </summary>
    [Fact]
    public async Task Webhook_FirstCall_MarksInvoicePaid()
    {
        var db  = CreateDb();
        var ref_ = Guid.NewGuid().ToString();
        var (invoice, _) = await SeedPaidScenarioAsync(db, ref_);
        var svc = MakeService(db);

        var ok = await svc.HandleWebhookAsync(MakeWebhookJwt(ref_));

        ok.Should().BeTrue();

        var updated = await db.Invoices.FindAsync(invoice.Id);
        updated!.Status.Should().Be(InvoiceStatus.Paid);
        updated.PaidAt.Should().NotBeNull();
    }

    /// <summary>
    /// Duplicate webhook: second call with same merchant_reference on an already-Paid
    /// invoice must return true without modifying PaidAt or creating side effects.
    /// </summary>
    [Fact]
    public async Task Webhook_DuplicateCall_ReturnsTrue_WithoutReprocessing()
    {
        var db  = CreateDb();
        var ref_ = Guid.NewGuid().ToString();
        var (invoice, _) = await SeedPaidScenarioAsync(
            db, ref_, invoiceStatus: InvoiceStatus.Paid);

        // Record the PaidAt that was set when the invoice was created
        var originalPaidAt = invoice.PaidAt;

        var svc = MakeService(db);

        var ok = await svc.HandleWebhookAsync(MakeWebhookJwt(ref_));

        ok.Should().BeTrue("idempotent — duplicate webhook must still succeed");

        var unchanged = await db.Invoices.FindAsync(invoice.Id);
        unchanged!.Status.Should().Be(InvoiceStatus.Paid, "status must not change");

        // PaidAt should remain null (was never set in seed) — verifying no UPDATE was issued
        unchanged.PaidAt.Should().Be(originalPaidAt, "PaidAt must not be overwritten on duplicate");
    }

    /// <summary>
    /// Webhook for unknown merchant_reference returns false without throwing.
    /// </summary>
    [Fact]
    public async Task Webhook_UnknownMerchantRef_ReturnsFalse()
    {
        var db  = CreateDb();
        var svc = MakeService(db);

        var ok = await svc.HandleWebhookAsync(
            MakeWebhookJwt(Guid.NewGuid().ToString()));

        ok.Should().BeFalse("no invoice matches this reference");
    }

    /// <summary>
    /// Webhook with non-paid status (e.g. "pending") returns false.
    /// </summary>
    [Fact]
    public async Task Webhook_NonPaidStatus_ReturnsFalse()
    {
        var db  = CreateDb();
        var ref_ = Guid.NewGuid().ToString();
        await SeedPaidScenarioAsync(db, ref_);
        var svc = MakeService(db);

        var ok = await svc.HandleWebhookAsync(
            MakeWebhookJwt(ref_, paymentStatus: "pending"));

        ok.Should().BeFalse("only 'paid' status should be processed");

        var invoice = await db.Invoices.FirstAsync(i => i.PaymentOrderId == ref_);
        invoice.Status.Should().NotBe(InvoiceStatus.Paid,
            "invoice must not be marked paid for a non-paid webhook");
    }

    /// <summary>
    /// Tampered / invalid JWT signature must be rejected by throwing
    /// WebhookSignatureException (→ HTTP 400). A bad signature is not a
    /// transient error and must not return a 200 that would suppress retries.
    /// </summary>
    [Fact]
    public async Task Webhook_TamperedJwt_Throws()
    {
        var db  = CreateDb();
        var svc = MakeService(db);

        // Build a valid JWT with a different key
        var wrongKey  = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("wrong-key-xxxxxxxxxxxxxxxxxxxxxxxxxxxx"));
        var wrongCred = new SigningCredentials(wrongKey, SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            claims: [new System.Security.Claims.Claim("data", "{\"merchant_reference\":\"x\",\"payment_status\":\"paid\"}")],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: wrongCred);
        var tampered = new JwtSecurityTokenHandler().WriteToken(jwt);

        await Assert.ThrowsAsync<Ruumly.Backend.Helpers.WebhookSignatureException>(
            () => svc.HandleWebhookAsync(tampered));
    }

    /// <summary>
    /// When the order is already Cancelled, webhook payment confirmation must
    /// NOT transition it to Sending — cancelled orders stay cancelled.
    /// </summary>
    [Fact]
    public async Task Webhook_PaidInvoice_CancelledOrder_DoesNotRedispatch()
    {
        var db  = CreateDb();
        var ref_ = Guid.NewGuid().ToString();
        var (_, order) = await SeedPaidScenarioAsync(
            db, ref_,
            invoiceStatus: InvoiceStatus.AwaitingPayment,
            orderStatus:   OrderStatus.Cancelled);
        var svc = MakeService(db);

        var ok = await svc.HandleWebhookAsync(MakeWebhookJwt(ref_));

        ok.Should().BeTrue();

        var updatedOrder = await db.Orders.FindAsync(order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.Cancelled,
            "cancelled orders must not be re-dispatched by a late payment webhook");
    }

    /// <summary>
    /// Amount verification: a 'paid' webhook whose grand_total does not match the
    /// invoice Amount must NOT mark the invoice Paid. Returns false and the invoice
    /// stays AwaitingPayment — guards against under/over-charge on a mismatched event.
    /// </summary>
    [Fact]
    public async Task Webhook_PaidStatus_AmountMismatch_DoesNotMarkPaid()
    {
        var db   = CreateDb();
        var ref_ = Guid.NewGuid().ToString();
        var (invoice, _) = await SeedPaidScenarioAsync(db, ref_); // seeded Amount = 95m
        var svc = MakeService(db);

        // Webhook reports 50 — does not match the billed 95.
        var ok = await svc.HandleWebhookAsync(MakeWebhookJwt(ref_, amount: 50m));

        ok.Should().BeFalse("a 'paid' webhook with a mismatched amount must be refused");

        var unchanged = await db.Invoices.FindAsync(invoice.Id);
        unchanged!.Status.Should().Be(InvoiceStatus.AwaitingPayment,
            "invoice must not be marked Paid on an amount-mismatched webhook");
        unchanged.PaidAt.Should().BeNull();
    }

    /// <summary>
    /// Terminal-state guard: a late 'cancelled' webhook arriving on an already-PAID
    /// invoice must leave it Paid and must NOT cancel the linked booking — a stale
    /// abandoned-checkout event must never downgrade a successful payment.
    /// </summary>
    [Fact]
    public async Task Webhook_LateCancelled_OnPaidInvoice_LeavesPaid_AndDoesNotCancelBooking()
    {
        var db   = CreateDb();
        var ref_ = Guid.NewGuid().ToString();
        var (invoice, _) = await SeedPaidScenarioAsync(
            db, ref_, invoiceStatus: InvoiceStatus.Paid);
        var svc = MakeService(db);

        var ok = await svc.HandleWebhookAsync(MakeWebhookJwt(ref_, paymentStatus: "cancelled"));

        ok.Should().BeTrue("stale 'cancelled' events are acknowledged (200) but not acted on");

        var unchangedInvoice = await db.Invoices.FindAsync(invoice.Id);
        unchangedInvoice!.Status.Should().Be(InvoiceStatus.Paid,
            "a late 'cancelled' must not downgrade a Paid invoice to Refunded");

        var booking = await db.Bookings.FirstAsync(b => b.Id == invoice.BookingId);
        booking.Status.Should().NotBe(BookingStatus.Cancelled,
            "a late 'cancelled' on a paid invoice must not cancel the booking");
    }
}
