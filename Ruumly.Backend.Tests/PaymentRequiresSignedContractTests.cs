using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Sign-then-pay acceptance criterion #1: a Montonio payment order cannot be created for a
/// booking that has no COMPLETED signed contract. The guard lives in
/// <see cref="MontonioPaymentService.CreatePaymentOrderAsync"/> and runs before any outbound
/// HTTP, so these tests never touch the network on the reject path.
/// </summary>
public class PaymentRequiresSignedContractTests
{
    private static RuumlyDbContext CreateDb() => TestDbContext.Create();

    private static MontonioPaymentService MakeService(RuumlyDbContext db, HttpClient? client = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Montonio:SecretKey"]  = "test-secret-key-that-is-long-enough-for-hs256",
                ["Montonio:AccessKey"]  = "test-access-key",
                ["Montonio:UseSandbox"] = "true",
                ["Montonio:ReturnUrl"]  = "https://ruumly.eu/pay/return",
                ["Montonio:NotifyUrl"]  = "https://ruumly.eu/api/payments/webhook",
            })
            .Build();

        return new MontonioPaymentService(
            db, config,
            new StubHttpClientFactory(client),
            NullLogger<MontonioPaymentService>.Instance,
            new StubHttpContext());
    }

    private sealed class StubHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class StubHttpClientFactory(HttpClient? client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client ?? new HttpClient();
    }

    // A handler that records whether it was ever invoked, and returns 502 so the
    // post-guard path fails fast without a real network call.
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public bool WasCalled { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("{}"),
            });
        }
    }

    private static async Task<Invoice> SeedAsync(RuumlyDbContext db, bool withCompletedContract)
    {
        var bookingId  = Guid.NewGuid();
        var supplierId = Guid.NewGuid();

        var listing = new Listing
        {
            Id          = Guid.NewGuid(),
            SupplierId  = supplierId,
            Type        = ListingType.Warehouse,
            Title       = "Test Warehouse",
            Address     = "Test St 1",
            City        = "Tallinn",
            PriceUnit   = "kuu",
            Description = "Test",
        };

        var booking = new Booking
        {
            Id         = bookingId,
            UserId     = Guid.NewGuid(),
            ListingId  = listing.Id,
            SupplierId = supplierId,
            StartDate  = DateTime.UtcNow.AddDays(5),
            Duration   = "1 kuu",
            Status     = BookingStatus.Pending,
            Total      = 95m,
        };

        var invoice = new Invoice
        {
            Id        = Guid.NewGuid(),
            BookingId = bookingId,
            Amount    = 95m,
            Status    = InvoiceStatus.Pending,
        };

        db.Listings.Add(listing);
        db.Bookings.Add(booking);
        db.Invoices.Add(invoice);

        if (withCompletedContract)
        {
            db.SignedContracts.Add(new SignedContract
            {
                Id          = Guid.NewGuid(),
                BookingId   = bookingId,
                Status      = "completed",
                TenantName  = "Mari",
                TenantEmail = "mari@test.ee",
            });
        }

        await db.SaveChangesAsync();
        return invoice;
    }

    [Fact]
    public async Task Initiate_Throws_When_No_Completed_Contract()
    {
        var db      = CreateDb();
        var invoice = await SeedAsync(db, withCompletedContract: false);
        var handler = new RecordingHandler();
        var svc     = MakeService(db, new HttpClient(handler));

        var act = async () => await svc.CreatePaymentOrderAsync(
            invoice.Id, "bank", "customer@test.ee", "et");

        await act.Should().ThrowAsync<ContractNotSignedException>();
        handler.WasCalled.Should().BeFalse("the guard rejects before any Montonio HTTP call");

        // Invoice must NOT have been moved to AwaitingPayment.
        var unchanged = await db.Invoices.FindAsync(invoice.Id);
        unchanged!.Status.Should().Be(InvoiceStatus.Pending);
    }

    [Fact]
    public async Task Initiate_With_Pending_Contract_Is_Rejected()
    {
        var db = CreateDb();
        var invoice = await SeedAsync(db, withCompletedContract: false);
        // A mid-signing (pending) contract is not enough — must be "completed".
        db.SignedContracts.Add(new SignedContract
        {
            Id = Guid.NewGuid(), BookingId = invoice.BookingId, Status = "pending",
            TenantName = "Mari", TenantEmail = "mari@test.ee",
        });
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        var act = async () => await svc.CreatePaymentOrderAsync(
            invoice.Id, "bank", "customer@test.ee", "et");

        await act.Should().ThrowAsync<ContractNotSignedException>();
    }

    [Fact]
    public async Task Initiate_Passes_Guard_When_Completed_Contract_Exists()
    {
        var db      = CreateDb();
        var invoice = await SeedAsync(db, withCompletedContract: true);
        var handler = new RecordingHandler();
        var svc     = MakeService(db, new HttpClient(handler));

        // The guard passes; the post-guard Montonio HTTP call is stubbed to fail (502),
        // which surfaces as InvalidOperationException (a different type than
        // ContractNotSignedException), proving the guard let the flow through.
        var act = async () => await svc.CreatePaymentOrderAsync(
            invoice.Id, "bank", "customer@test.ee", "et");

        await act.Should().ThrowAsync<InvalidOperationException>();
        handler.WasCalled.Should().BeTrue("with a signed contract the flow proceeds to Montonio");

        // Booking stays Pending until the webhook confirms payment.
        var booking = await db.Bookings.FindAsync(invoice.BookingId);
        booking!.Status.Should().Be(BookingStatus.Pending);
    }

    [Fact]
    public async Task Later_Payment_Does_Not_Require_Signed_Contract()
    {
        var db      = CreateDb();
        var invoice = await SeedAsync(db, withCompletedContract: false);
        var svc     = MakeService(db);

        // "later" / rebate path never reaches Montonio and is not gated by the contract.
        var result = await svc.CreatePaymentOrderAsync(
            invoice.Id, "later", "customer@test.ee", "et");

        result.Should().BeEmpty();
        var updated = await db.Invoices.FindAsync(invoice.Id);
        updated!.PaymentMethod.Should().Be("later");
    }
}
