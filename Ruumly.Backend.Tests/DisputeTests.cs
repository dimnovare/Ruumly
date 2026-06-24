using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Wave L — trust-and-safety disputes. A customer/partner raises a claim against a
/// booking; an admin resolves it. Ownership-gated; admin resolution notifies the raiser.
/// </summary>
public class DisputeTests
{
    private sealed class CapturingNotifications : INotificationService
    {
        public List<(Guid UserId, string Title)> Created { get; } = [];
        public Task<PaginatedResult<NotificationDto>> GetAllAsync(Guid userId, int page = 1, int limit = 50)
            => Task.FromResult(new PaginatedResult<NotificationDto>([], 0, page, limit, false));
        public Task MarkReadAsync(Guid id, Guid userId) => Task.CompletedTask;
        public Task MarkAllReadAsync(Guid userId) => Task.CompletedTask;
        public Task CreateAsync(Guid userId, NotificationType type, string title, string desc,
            string? actionUrl = null, string? entityId = null, string? entityType = null)
        { Created.Add((userId, title)); return Task.CompletedTask; }
    }

    private sealed class NoOpEmailQueue : IBackgroundEmailQueue
    {
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null) { }
        public void EnqueueVerificationEmail(Guid userId) { }
    }

    private static ControllerContext Ctx(Guid userId, UserRole role, string email = "u@x.ee") =>
        new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Role, role.ToString()),
                    new Claim(ClaimTypes.Email, email),
                ], "test")),
            },
        };

    private static (Supplier supplier, User customer, Booking booking) Seed(RuumlyDbContext db)
    {
        var supplier = new Supplier { Id = Guid.NewGuid(), Name = "Mover", ContactName = "C", ContactEmail = "s@x.ee", ContactPhone = "1" };
        var listing  = new Listing { Id = Guid.NewGuid(), SupplierId = supplier.Id, Type = ListingType.Trailer, Title = "Trailer A", City = "Tallinn", IsActive = true };
        var customer = new User { Id = Guid.NewGuid(), Name = "Cust", Email = "cust@x.ee", Role = UserRole.Customer };
        var booking  = new Booking
        {
            Id = Guid.NewGuid(), UserId = customer.Id, ListingId = listing.Id, SupplierId = supplier.Id,
            ContactName = "Cust", ContactEmail = "cust@x.ee", ContactPhone = "1",
            StartDate = DateTime.UtcNow, Status = BookingStatus.Completed, Total = 60m,
        };
        db.Suppliers.Add(supplier); db.Listings.Add(listing); db.Users.Add(customer); db.Bookings.Add(booking);
        db.SaveChanges();
        return (supplier, customer, booking);
    }

    private static DisputesController MakeRaise(RuumlyDbContext db, CapturingNotifications notif, Guid userId, UserRole role, string email = "u@x.ee")
        => new(db, notif, new NoOpEmailQueue()) { ControllerContext = Ctx(userId, role, email) };

    private static AdminDisputesController MakeAdmin(RuumlyDbContext db, CapturingNotifications notif)
        => new(db, notif, Microsoft.Extensions.Logging.Abstractions.NullLogger<AdminDisputesController>.Instance)
            { ControllerContext = Ctx(Guid.NewGuid(), UserRole.Admin, "admin@x.ee") };

    // ─── Admin-confirmed dispute → refund ────────────────────────────────────

    [Fact]
    public async Task Admin_ResolveWithRefund_MarksInvoicePendingRefund_AndCancelsPayout()
    {
        var db = TestDbContext.Create();
        var (supplier, customer, booking) = Seed(db);
        var order = new Order
        {
            Id = Guid.NewGuid(), BookingId = booking.Id, SupplierId = supplier.Id, ListingId = booking.ListingId,
            ListingTitle = "Trailer A", ListingType = ListingType.Trailer, City = "Tallinn",
            StartDate = DateTime.UtcNow, Duration = "1 day", CustomerName = "Cust", CustomerEmail = "cust@x.ee",
            CustomerPhone = "1", IntegrationType = IntegrationType.Manual, Status = OrderStatus.Confirmed,
            Total = 60m, SupplierPrice = 50m,
        };
        var invoice = new Invoice { Id = Guid.NewGuid(), BookingId = booking.Id, Amount = 60m, Status = InvoiceStatus.Paid };
        var payout = new PayoutEntry
        {
            Id = Guid.NewGuid(), SupplierId = supplier.Id, OrderId = order.Id,
            SupplierAmount = 50m, PlatformMargin = 10m, Status = PayoutStatus.Accrued,
        };
        var dispute = new Dispute
        {
            Id = Guid.NewGuid(), BookingId = booking.Id, SupplierId = supplier.Id,
            RaisedByUserId = customer.Id, RaisedByRole = UserRole.Customer, Type = DisputeType.Deposit,
            Status = DisputeStatus.Open, Subject = "Deposit not returned", ContactEmail = "cust@x.ee",
            AmountClaimed = 30m, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.Orders.Add(order); db.Invoices.Add(invoice); db.PayoutEntries.Add(payout); db.Disputes.Add(dispute);
        await db.SaveChangesAsync();
        var notif = new CapturingNotifications();

        var result = await MakeAdmin(db, notif)
            .Update(dispute.Id, new UpdateDisputeRequest("resolved", AdminNotes: null, Resolution: "Deposit refunded", IssueRefund: true));

        result.Should().BeOfType<OkObjectResult>();
        (await db.Invoices.FindAsync(invoice.Id))!.Status.Should().Be(InvoiceStatus.PendingRefund);
        (await db.PayoutEntries.FindAsync(payout.Id))!.Status.Should().Be(PayoutStatus.Cancelled,
            "a dispute-driven refund must not pay the supplier");
        (await db.Disputes.FindAsync(dispute.Id))!.Status.Should().Be(DisputeStatus.Resolved);
        notif.Created.Should().Contain(n => n.UserId == customer.Id, "the customer is notified of the refund");
    }

    [Fact]
    public async Task Admin_ResolveWithRefund_NoPaidInvoice_400_NothingChanges()
    {
        var db = TestDbContext.Create();
        var (supplier, customer, booking) = Seed(db);
        // Invoice exists but is NOT paid → refund must be rejected, dispute unchanged.
        db.Invoices.Add(new Invoice { Id = Guid.NewGuid(), BookingId = booking.Id, Amount = 60m, Status = InvoiceStatus.Pending });
        var dispute = new Dispute
        {
            Id = Guid.NewGuid(), BookingId = booking.Id, SupplierId = supplier.Id,
            RaisedByUserId = customer.Id, RaisedByRole = UserRole.Customer, Type = DisputeType.Deposit,
            Status = DisputeStatus.Open, Subject = "x", ContactEmail = "c@x.ee",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.Disputes.Add(dispute);
        await db.SaveChangesAsync();

        var result = await MakeAdmin(db, new CapturingNotifications())
            .Update(dispute.Id, new UpdateDisputeRequest("resolved", null, null, IssueRefund: true));

        result.Should().BeOfType<BadRequestObjectResult>();
        (await db.Disputes.FindAsync(dispute.Id))!.Status.Should().Be(DisputeStatus.Open, "resolve+refund is atomic — neither applies");
    }

    [Fact]
    public async Task Raise_OwnBooking_CreatesOpenDispute_AndNotifiesAdmin()
    {
        var db = TestDbContext.Create();
        var (supplier, customer, booking) = Seed(db);
        db.Users.Add(new User { Id = Guid.NewGuid(), Name = "Admin", Email = "a@x.ee", Role = UserRole.Admin });
        await db.SaveChangesAsync();
        var notif = new CapturingNotifications();

        var result = await MakeRaise(db, notif, customer.Id, UserRole.Customer)
            .Raise(new RaiseDisputeRequest(booking.Id, null, "deposit", "Deposit not returned",
                   Description: "Trailer returned clean", AmountClaimed: 50m, Evidence: ["photo1"]));

        result.Should().BeOfType<OkObjectResult>();
        var d = db.Disputes.Single();
        d.SupplierId.Should().Be(supplier.Id);
        d.Type.Should().Be(DisputeType.Deposit);
        d.Status.Should().Be(DisputeStatus.Open);
        d.AmountClaimed.Should().Be(50m);
        d.RaisedByUserId.Should().Be(customer.Id);
        notif.Created.Should().ContainSingle(n => n.Title == "New dispute raised");
    }

    [Fact]
    public async Task Raise_OthersBooking_403_NoDispute()
    {
        var db = TestDbContext.Create();
        var (_, _, booking) = Seed(db);

        var result = await MakeRaise(db, new CapturingNotifications(), Guid.NewGuid(), UserRole.Customer)
            .Raise(new RaiseDisputeRequest(booking.Id, null, "damage", "Damaged goods"));

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        db.Disputes.Should().BeEmpty();
    }

    [Fact]
    public async Task Raise_InvalidType_400()
    {
        var db = TestDbContext.Create();
        var (_, customer, booking) = Seed(db);

        var result = await MakeRaise(db, new CapturingNotifications(), customer.Id, UserRole.Customer)
            .Raise(new RaiseDisputeRequest(booking.Id, null, "nonsense", "x"));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Admin_Resolve_SetsResolvedAt_AndNotifiesRaiser()
    {
        var db = TestDbContext.Create();
        var (supplier, customer, booking) = Seed(db);
        var dispute = new Dispute
        {
            Id = Guid.NewGuid(), BookingId = booking.Id, SupplierId = supplier.Id,
            RaisedByUserId = customer.Id, RaisedByRole = UserRole.Customer,
            Type = DisputeType.Damage, Status = DisputeStatus.Open,
            Subject = "Scratch", ContactEmail = "cust@x.ee", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.Disputes.Add(dispute);
        await db.SaveChangesAsync();
        var notif = new CapturingNotifications();

        var result = await MakeAdmin(db, notif)
            .Update(dispute.Id, new UpdateDisputeRequest("resolved", AdminNotes: "refunded deposit", Resolution: "Deposit refunded"));

        result.Should().BeOfType<OkObjectResult>();
        var fresh = db.Disputes.Single();
        fresh.Status.Should().Be(DisputeStatus.Resolved);
        fresh.ResolvedAt.Should().NotBeNull();
        fresh.Resolution.Should().Be("Deposit refunded");
        notif.Created.Should().ContainSingle(n => n.UserId == customer.Id && n.Title == "Dispute resolved");
    }

    [Fact]
    public async Task Admin_GetAll_FiltersByStatus_AndEnriches()
    {
        var db = TestDbContext.Create();
        var (supplier, customer, booking) = Seed(db);
        db.Disputes.AddRange(
            new Dispute { Id = Guid.NewGuid(), BookingId = booking.Id, SupplierId = supplier.Id, RaisedByUserId = customer.Id,
                RaisedByRole = UserRole.Customer, Type = DisputeType.NoShow, Status = DisputeStatus.Open, Subject = "No show",
                ContactEmail = "cust@x.ee", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Dispute { Id = Guid.NewGuid(), BookingId = booking.Id, SupplierId = supplier.Id, RaisedByUserId = customer.Id,
                RaisedByRole = UserRole.Customer, Type = DisputeType.Damage, Status = DisputeStatus.Resolved, Subject = "Old",
                ContactEmail = "cust@x.ee", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var result = await MakeAdmin(db, new CapturingNotifications()).GetAll(status: "open");

        var ok    = result.Should().BeOfType<OkObjectResult>().Subject;
        var total = (int)ok.Value!.GetType().GetProperty("total")!.GetValue(ok.Value!)!;
        total.Should().Be(1, "only the Open dispute matches the filter");
    }
}
