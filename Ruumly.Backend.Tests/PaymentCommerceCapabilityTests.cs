using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

public class PaymentCommerceCapabilityTests
{
    private static RuumlyDbContext CreateDb() => TestDbContext.Create();

    [Fact]
    public async Task Initiate_Rejects_Ruumly_Payment_When_Supplier_Disabled()
    {
        await using var db = CreateDb();
        var (invoice, customer) = await SeedInvoiceAsync(
            db,
            supplier =>
            {
                supplier.BookingEnabled = true;
                supplier.RuumlyPaymentEnabled = false;
            });
        var paymentService = new RecordingPaymentService();
        var controller = MakeController(db, paymentService, customer.Id);

        var result = await controller.Initiate(new PaymentsController.InitiatePaymentRequest(
            invoice.Id,
            PaymentMethod: "bank",
            CustomerEmail: customer.Email,
            Locale: "en"));

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new
        {
            error = ErrorMessages.Get("RUUMLY_PAYMENT_DISABLED", "et")
        });
        paymentService.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Initiate_Rejects_Direct_Payment_When_Supplier_Disabled()
    {
        await using var db = CreateDb();
        var (invoice, customer) = await SeedInvoiceAsync(
            db,
            supplier =>
            {
                supplier.BookingEnabled = true;
                supplier.DirectPaymentEnabled = false;
            });
        var paymentService = new RecordingPaymentService();
        var controller = MakeController(db, paymentService, customer.Id);

        var result = await controller.Initiate(new PaymentsController.InitiatePaymentRequest(
            invoice.Id,
            PaymentMethod: "later",
            CustomerEmail: customer.Email,
            Locale: "en"));

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new
        {
            error = ErrorMessages.Get("DIRECT_PAYMENT_DISABLED", "et")
        });
        paymentService.Calls.Should().Be(0);
    }

    private static PaymentsController MakeController(
        RuumlyDbContext db,
        IPaymentService paymentService,
        Guid userId)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Role, UserRole.Customer.ToString()),
                    new Claim(ClaimTypes.Email, "customer@test.ee"),
                ],
                authenticationType: "test"))
        };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        return new PaymentsController(
            paymentService,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PaymentsController>.Instance,
            db,
            accessor)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static async Task<(Invoice invoice, User customer)> SeedInvoiceAsync(
        RuumlyDbContext db,
        Action<Supplier> configureSupplier)
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
            RegistryCode = "PAY001",
            ContactName = "Partner Contact",
            ContactEmail = "supplier@test.ee",
            ContactPhone = "+37255555555",
            BillingModel = BillingModel.Marketplace,
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
            Supplier = supplier,
            Listing = listing,
            User = customer,
        };
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            Booking = booking,
            Amount = 100m,
            Description = "Test invoice",
        };

        db.Users.Add(customer);
        db.Suppliers.Add(supplier);
        db.Listings.Add(listing);
        db.Bookings.Add(booking);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        return (invoice, customer);
    }

    private sealed class RecordingPaymentService : IPaymentService
    {
        public int Calls { get; private set; }

        public Task<string> CreatePaymentOrderAsync(
            Guid invoiceId,
            string paymentMethod,
            string customerEmail,
            string customerLocale)
        {
            Calls++;
            return Task.FromResult("https://pay.test");
        }

        public Task<bool> HandleWebhookAsync(string token) => Task.FromResult(true);
    }
}
