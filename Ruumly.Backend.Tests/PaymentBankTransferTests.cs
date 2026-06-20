using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;
using Xunit;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Coverage for the bank-transfer branch of <see cref="PaymentsController.Initiate"/>
/// (method "bank_transfer"). The branch is a no-PSP, manual-reconciliation path that
/// returns payment INSTRUCTIONS instead of a redirect URL. It requires:
///   (1) the partner opted into direct payment (Supplier.DirectPaymentEnabled),
///   (2) the platform enabled bank transfer (PlatformSettings "bankTransfer.enabled"),
///   (3) the rental is signed first (a completed SignedContract).
/// Only then does it flip the invoice to AwaitingPayment and emit the instructions.
/// The controller is invoked directly with a fake IPaymentService that records calls —
/// the bank-transfer branch must never touch the payment service.
/// </summary>
public class PaymentBankTransferTests
{
    private static RuumlyDbContext CreateDb() => TestDbContext.Create();

    [Fact]
    public async Task BankTransfer_Rejected_When_DirectPayment_Disabled()
    {
        await using var db = CreateDb();
        var (invoice, customer) = await SeedAsync(
            db,
            configureSupplier: s => s.DirectPaymentEnabled = false,
            bankTransferEnabled: true,
            withCompletedContract: true,
            withIban: true);
        var paymentService = new RecordingPaymentService();
        var controller = MakeController(db, paymentService, customer.Id);

        var result = await controller.Initiate(Req(invoice, customer));

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value.Should().BeEquivalentTo(new
        {
            error = ErrorMessages.Get("DIRECT_PAYMENT_DISABLED", "et"),
        });
        paymentService.Calls.Should().Be(0);

        // Invoice untouched — not promoted to AwaitingPayment.
        (await db.Invoices.FindAsync(invoice.Id))!.Status.Should().Be(InvoiceStatus.Pending);
    }

    [Fact]
    public async Task BankTransfer_Rejected_When_Platform_Setting_Not_True()
    {
        await using var db = CreateDb();
        var (invoice, customer) = await SeedAsync(
            db,
            configureSupplier: s => s.DirectPaymentEnabled = true,
            bankTransferEnabled: false,   // PlatformSettings "bankTransfer.enabled" != true
            withCompletedContract: true,
            withIban: true);
        var paymentService = new RecordingPaymentService();
        var controller = MakeController(db, paymentService, customer.Id);

        var result = await controller.Initiate(Req(invoice, customer));

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value.Should().BeEquivalentTo(new
        {
            error = ErrorMessages.Get("DIRECT_PAYMENT_DISABLED", "et"),
        });
        paymentService.Calls.Should().Be(0);
        (await db.Invoices.FindAsync(invoice.Id))!.Status.Should().Be(InvoiceStatus.Pending);
    }

    [Fact]
    public async Task BankTransfer_Rejected_With_Conflict_When_No_Signed_Contract()
    {
        await using var db = CreateDb();
        var (invoice, customer) = await SeedAsync(
            db,
            configureSupplier: s => s.DirectPaymentEnabled = true,
            bankTransferEnabled: true,
            withCompletedContract: false,   // sign-then-pay gate
            withIban: true);
        var paymentService = new RecordingPaymentService();
        var controller = MakeController(db, paymentService, customer.Id);

        var result = await controller.Initiate(Req(invoice, customer));

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value.Should().BeEquivalentTo(new
        {
            code  = ContractNotSignedException.Code,   // "CONTRACT_NOT_SIGNED"
            error = "This rental must be signed before payment can be made.",
        });
        paymentService.Calls.Should().Be(0);
        (await db.Invoices.FindAsync(invoice.Id))!.Status.Should().Be(InvoiceStatus.Pending);
    }

    [Fact]
    public async Task BankTransfer_HappyPath_Returns_Instructions_And_Sets_AwaitingPayment()
    {
        await using var db = CreateDb();
        var (invoice, customer) = await SeedAsync(
            db,
            configureSupplier: s => s.DirectPaymentEnabled = true,
            bankTransferEnabled: true,
            withCompletedContract: true,
            withIban: true);
        var paymentService = new RecordingPaymentService();
        var controller = MakeController(db, paymentService, customer.Id);

        var result = await controller.Initiate(Req(invoice, customer));

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        paymentService.Calls.Should().Be(0, "bank transfer never goes through the PSP service");

        // Invoice flipped to AwaitingPayment (manual reconciliation by admin on receipt).
        (await db.Invoices.FindAsync(invoice.Id))!.Status.Should().Be(InvoiceStatus.AwaitingPayment);
        (await db.Invoices.FindAsync(invoice.Id))!.PaymentMethod.Should().Be("bank_transfer");

        // The instructions object: available=true (IBAN set), correct amount/currency, and a
        // reference of "RUUMLY-" + first 8 hex chars of the booking id (uppercased).
        var expectedReference = "RUUMLY-" + invoice.BookingId.ToString("N")[..8].ToUpperInvariant();
        ok.Value.Should().BeEquivalentTo(new
        {
            paymentUrl   = (string?)null,
            bankTransfer = new
            {
                available   = true,
                amount      = invoice.Amount,
                currency    = "EUR",
                reference   = expectedReference,
                accountName = "Ruumly OU",
                iban        = "EE001234567890123456",
                bic         = "LHVBEE22",
                bankName    = "LHV Pank",
            },
        });
    }

    [Fact]
    public async Task BankTransfer_Available_False_When_Platform_Iban_Unset()
    {
        await using var db = CreateDb();
        var (invoice, customer) = await SeedAsync(
            db,
            configureSupplier: s => s.DirectPaymentEnabled = true,
            bankTransferEnabled: true,
            withCompletedContract: true,
            withIban: false);    // "bankTransfer.iban" never set
        var paymentService = new RecordingPaymentService();
        var controller = MakeController(db, paymentService, customer.Id);

        var result = await controller.Initiate(Req(invoice, customer));

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        // available reflects whether the platform IBAN is configured.
        ok.Value.Should().BeEquivalentTo(new
        {
            bankTransfer = new { available = false, iban = "" },
        }, opts => opts.ExcludingMissingMembers());

        // The branch still ran (invoice promoted) — only "available" reflects the missing IBAN.
        (await db.Invoices.FindAsync(invoice.Id))!.Status.Should().Be(InvoiceStatus.AwaitingPayment);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static PaymentsController.InitiatePaymentRequest Req(Invoice invoice, User customer) =>
        new(invoice.Id, PaymentMethod: "bank_transfer", CustomerEmail: customer.Email, Locale: "en");

    private static PaymentsController MakeController(
        RuumlyDbContext db, IPaymentService paymentService, Guid userId)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Role, UserRole.Customer.ToString()),
                    new Claim(ClaimTypes.Email, "customer@test.ee"),
                ],
                authenticationType: "test")),
        };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        return new PaymentsController(
            paymentService,
            NullLogger<PaymentsController>.Instance,
            db,
            accessor)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private static async Task<(Invoice invoice, User customer)> SeedAsync(
        RuumlyDbContext db,
        Action<Supplier> configureSupplier,
        bool bankTransferEnabled,
        bool withCompletedContract,
        bool withIban)
    {
        var customer = new User
        {
            Id = Guid.NewGuid(),
            Name = "Customer",
            Email = "customer@test.ee",
            Role = UserRole.Customer,
            EmailVerified = true,
        };
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Name = "Test Supplier",
            RegistryCode = "BT001",
            ContactName = "Partner Contact",
            ContactEmail = "supplier@test.ee",
            ContactPhone = "+37255555555",
            BillingModel = BillingModel.Marketplace,
            BookingEnabled = true,
        };
        configureSupplier(supplier);

        var listing = new Listing
        {
            Id = Guid.NewGuid(),
            SupplierId = supplier.Id,
            Type = ListingType.Warehouse,
            Title = "Storage unit",
            Address = "Test 1",
            City = "Tallinn",
            PriceFrom = 100m,
            PriceUnit = "month",
            IsActive = true,
            AvailableNow = true,
        };
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            UserId = customer.Id,
            ListingId = listing.Id,
            SupplierId = supplier.Id,
            StartDate = DateTime.UtcNow.Date.AddDays(1),
            Duration = "monthly",
            BasePrice = 100m,
            PlatformPrice = 100m,
            Total = 100m,
            Status = BookingStatus.Pending,
            Supplier = supplier,
            Listing = listing,
            User = customer,
        };
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            Booking = booking,
            Amount = 95m,
            Status = InvoiceStatus.Pending,
            Description = "Test invoice",
        };

        db.Users.Add(customer);
        db.Suppliers.Add(supplier);
        db.Listings.Add(listing);
        db.Bookings.Add(booking);
        db.Invoices.Add(invoice);

        if (withCompletedContract)
        {
            db.SignedContracts.Add(new SignedContract
            {
                Id = Guid.NewGuid(),
                BookingId = booking.Id,
                Status = "completed",
                TenantName = "Mari",
                TenantEmail = "mari@test.ee",
            });
        }

        if (bankTransferEnabled)
            db.PlatformSettings.Add(Setting("bankTransfer.enabled", "true"));

        if (withIban)
        {
            db.PlatformSettings.Add(Setting("bankTransfer.iban", "EE001234567890123456"));
            db.PlatformSettings.Add(Setting("bankTransfer.accountName", "Ruumly OU"));
            db.PlatformSettings.Add(Setting("bankTransfer.bic", "LHVBEE22"));
            db.PlatformSettings.Add(Setting("bankTransfer.bankName", "LHV Pank"));
        }

        await db.SaveChangesAsync();
        return (invoice, customer);
    }

    private static PlatformSetting Setting(string key, string value) =>
        new() { Key = key, Value = value, UpdatedBy = "test" };

    private sealed class RecordingPaymentService : IPaymentService
    {
        public int Calls { get; private set; }

        public Task<string> CreatePaymentOrderAsync(
            Guid invoiceId, string paymentMethod, string customerEmail, string customerLocale)
        {
            Calls++;
            return Task.FromResult("https://pay.test");
        }

        public Task<bool> HandleWebhookAsync(string token) => Task.FromResult(true);
    }
}
