using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;
using Xunit;

namespace Ruumly.Backend.Tests.Integration;

/// <summary>
/// Three lifecycle paths exercised against the REAL Npgsql provider (isolated temp DB,
/// per-test transaction rollback). Each targets an enum→string SQL translation or a
/// filtered-Include/Any pattern that EF InMemory cannot reproduce — the same class of
/// bug that silently returned 0 rows in prod during the lowercase-casing incident.
///   (1) AdminDisputesController.GetAll(status: "open") — DisputeStatus enum filter.
///   (2) LocationsController.GetAll(type: "warehouse")  — the `Listings.Any(...)`
///       filtered-Include + Any pattern that broke locations-by-type.
///   (3) OrderService.ConfirmAsync — the money path: Accrued → Pending payout promotion
///       with the full Supplier→User→Listing→Booking→Order→PayoutEntry FK graph enforced.
/// No-ops when no Postgres is reachable (CI without a DB stays green).
/// </summary>
[Collection("Postgres integration")]
public class LifecycleNpgsqlTests(PostgresIntegrationFixture pg)
{
    // ── Shared stubs (mirror DisputeTests / LaunchGuardTests / PayoutLifecycle) ──
    private sealed class NoOpNotifications : INotificationService
    {
        public Task<PaginatedResult<NotificationDto>> GetAllAsync(Guid userId, int page = 1, int limit = 50)
            => Task.FromResult(new PaginatedResult<NotificationDto>([], 0, page, limit, false));
        public Task MarkReadAsync(Guid id, Guid userId) => Task.CompletedTask;
        public Task MarkAllReadAsync(Guid userId) => Task.CompletedTask;
        public Task CreateAsync(Guid userId, NotificationType type, string title, string desc,
            string? actionUrl = null, string? entityId = null, string? entityType = null) => Task.CompletedTask;
    }

    private static string Uniq() => Guid.NewGuid().ToString("N")[..10];

    // Npgsql ENFORCES Supplier's unique RegistryCode + ContactEmail — every seed needs a
    // fresh value or the second insert violates the unique index.
    private static Supplier MakeSupplier()
    {
        var u = Uniq();
        return new Supplier
        {
            Id = Guid.NewGuid(), Name = "Sup", RegistryCode = u, ContactName = "C",
            ContactEmail = $"s{u}@t.ee", ContactPhone = "1", IsActive = true,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // (1) AdminDisputesController.GetAll(status: "open") — DisputeStatus enum filter.
    // ─────────────────────────────────────────────────────────────────────────────

    private static AdminDisputesController MakeAdminDisputes(RuumlyDbContext db) =>
        new(db, new NoOpNotifications(), NullLogger<AdminDisputesController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                        new Claim(ClaimTypes.Role, UserRole.Admin.ToString()),
                        new Claim(ClaimTypes.Email, "admin@x.ee"),
                    ], "test")),
                },
            },
        };

    [Fact]
    public async Task GetAll_StatusOpen_ReturnsOnlyOpenDisputes_UnderNpgsql()
    {
        if (!pg.Available) return;
        await using var db = pg.NewContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var supplier = MakeSupplier();
        db.Suppliers.Add(supplier);
        // BookingId is left null so the enrichment join stays trivial — the test targets
        // the DisputeStatus enum→string WHERE filter, not the supplier/listing lookups.
        db.Disputes.AddRange(
            new Dispute
            {
                Id = Guid.NewGuid(), SupplierId = supplier.Id, RaisedByRole = UserRole.Customer,
                Type = DisputeType.NoShow, Status = DisputeStatus.Open, Subject = "No show",
                ContactEmail = "cust@x.ee", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            },
            new Dispute
            {
                Id = Guid.NewGuid(), SupplierId = supplier.Id, RaisedByRole = UserRole.Customer,
                Type = DisputeType.Damage, Status = DisputeStatus.Resolved, Subject = "Old",
                ContactEmail = "cust@x.ee", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();

        // The exact enum→string filter: WHERE "Status" = 'Open'. Same bug class as the
        // ListingType casing incident — must match the Open row and exclude the Resolved one.
        var result = await MakeAdminDisputes(db).GetAll(status: "open");

        var ok    = result.Should().BeOfType<OkObjectResult>().Subject;
        var total = (int)ok.Value!.GetType().GetProperty("total")!.GetValue(ok.Value!)!;
        total.Should().Be(1, "only the Open dispute matches the DisputeStatus enum filter under real SQL");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // (2) LocationsController.GetAll(type: "warehouse") — the filtered-Include + Any
    //     pattern (`l.Listings.Any(u => u.Type == lt && u.IsActive)`) that returned 0
    //     during the casing bug.
    // ─────────────────────────────────────────────────────────────────────────────

    // Unauthenticated principal → isAuthenticated == false, so GetAll takes the public
    // (anonymous) path and never calls User.GetUserRole() (which throws on a missing
    // Role claim). That public path keeps the non-synthetic/active filtering we seed for.
    private static LocationsController MakeLocations(RuumlyDbContext db) =>
        new(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) },
            },
        };

    private static Listing MakeListing(Guid supplierId, ListingType type, Guid? locationId = null) => new()
    {
        Id = Guid.NewGuid(), SupplierId = supplierId, Type = type, Title = $"{type}",
        Address = "St", City = "Tallinn", PriceFrom = 50m, PriceUnit = "kuu",
        IsActive = true, AvailableNow = true, Description = "x", LocationId = locationId,
    };

    [Fact]
    public async Task GetAll_TypeWarehouse_ReturnsOnlyWarehouseLocations_UnderNpgsql()
    {
        if (!pg.Available) return;
        await using var db = pg.NewContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var supplier = MakeSupplier();
        db.Suppliers.Add(supplier);

        // A real (non-synthetic, active) location holding a warehouse listing.
        var warehouseLoc = new SupplierLocation
        {
            Id = Guid.NewGuid(), SupplierId = supplier.Id, Name = "Warehouse Hub", Address = "St",
            City = "Tallinn", IsSynthetic = false, IsActive = true,
        };
        // A separate real location holding only a moving listing — must NOT match type=warehouse.
        var movingLoc = new SupplierLocation
        {
            Id = Guid.NewGuid(), SupplierId = supplier.Id, Name = "Moving Hub", Address = "St",
            City = "Tallinn", IsSynthetic = false, IsActive = true,
        };
        db.SupplierLocations.AddRange(warehouseLoc, movingLoc);
        db.Listings.Add(MakeListing(supplier.Id, ListingType.Warehouse, locationId: warehouseLoc.Id));
        db.Listings.Add(MakeListing(supplier.Id, ListingType.Moving,    locationId: movingLoc.Id));
        await db.SaveChangesAsync();

        // The controller's GET action with the type filter — exercises the exact
        // `query.Where(l => l.Listings.Any(u => u.Type == lt && u.IsActive))` translation.
        var result = await MakeLocations(db).GetAll(city: null, type: "warehouse");

        var ok   = result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = ok.Value.Should().BeAssignableTo<List<SupplierLocationDto>>().Subject;
        dtos.Should().ContainSingle(d => d.Id == warehouseLoc.Id,
            "only the location with an active warehouse listing must pass the Listings.Any(Type==Warehouse) filter");
        dtos.Should().NotContain(d => d.Id == movingLoc.Id,
            "a moving-only location must be excluded by the type=warehouse filter");

        // Belt-and-suspenders: assert the raw EF translation independently of controller plumbing.
        var rawCount = await db.SupplierLocations
            .Where(l => l.Listings.Any(u => u.Type == ListingType.Warehouse && u.IsActive))
            .CountAsync();
        rawCount.Should().Be(1, "the filtered Listings.Any(...) query must translate and return exactly the warehouse location");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // (3) OrderService.ConfirmAsync promotes an Accrued payout to Pending — the money
    //     path — with the FULL FK graph enforced by Npgsql.
    // ─────────────────────────────────────────────────────────────────────────────

    private sealed class NoOpDispatch : IIntegrationDispatchService
    {
        public Task DispatchAsync(Order order, Supplier supplier) => Task.CompletedTask;
    }

    private sealed class NoOpInvoice : IInvoiceService
    {
        public Task<PaginatedResult<InvoiceDto>> GetAllAsync(Guid userId, UserRole role, int page = 1, int limit = 100)
            => Task.FromResult(new PaginatedResult<InvoiceDto>([], 0, page, limit, false));
        public Task<InvoiceDto?> GetByBookingIdAsync(Guid bookingId, Guid userId, UserRole role)
            => Task.FromResult<InvoiceDto?>(null);
        public Task<InvoiceDto> GenerateAsync(Guid bookingId, string? paymentMethod = null)
            => Task.FromResult(new InvoiceDto(bookingId, bookingId, 0m, "pending", "2026-01-01", null, ""));
        public Task<InvoiceDto> MarkPaidAsync(Guid id, string? paymentReference = null)
            => Task.FromResult(new InvoiceDto(id, id, 0m, "paid", "2026-01-01", "2026-01-01", ""));
    }

    private sealed class NoOpEmail : IEmailSender
    {
        public Task SendAsync(string to, string subject, string textBody, string? htmlBody = null)
            => Task.CompletedTask;
    }

    private sealed class NoOpHttp : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } }

    private static OrderService MakeOrderService(RuumlyDbContext db) =>
        new(db,
            new NoOpDispatch(), new NoOpNotifications(), new NoOpInvoice(), new NoOpEmail(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["AppUrl"] = "https://test.ruumly.eu" })
                .Build(),
            NullLogger<OrderService>.Instance,
            new NoOpHttp());

    [Fact]
    public async Task ConfirmAsync_PromotesAccruedPayoutToPending_UnderNpgsql()
    {
        if (!pg.Available) return;
        await using var db = pg.NewContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        // Full FK graph — Npgsql enforces every relationship (a missing row fails the insert).
        var supplier = MakeSupplier();
        var customer = new User
        {
            Id = Guid.NewGuid(), Name = "Cust", Email = $"c{Uniq()}@t.ee", Role = UserRole.Customer,
        };
        var listing = MakeListing(supplier.Id, ListingType.Warehouse);
        var booking = new Booking
        {
            Id = Guid.NewGuid(), UserId = customer.Id, ListingId = listing.Id, SupplierId = supplier.Id,
            ContactName = "Cust", ContactEmail = "c@t.ee", ContactPhone = "1",
            StartDate = DateTime.UtcNow, Duration = "1 kuu", Status = BookingStatus.AwaitingConfirmation, Total = 95m,
        };
        var order = new Order
        {
            Id = Guid.NewGuid(), BookingId = booking.Id, SupplierId = supplier.Id, ListingId = listing.Id,
            ListingTitle = "W", ListingType = ListingType.Warehouse, City = "Tallinn",
            StartDate = DateTime.UtcNow, Duration = "1 kuu", CustomerName = "Cust", CustomerEmail = "c@t.ee",
            CustomerPhone = "1", IntegrationType = IntegrationType.Manual, Status = OrderStatus.Sent,
            Total = 95m, SupplierPrice = 87m,
        };
        var payout = new PayoutEntry
        {
            Id = Guid.NewGuid(), SupplierId = supplier.Id, OrderId = order.Id,
            SupplierAmount = 87m, PlatformMargin = 8m, Status = PayoutStatus.Accrued,
        };

        db.Suppliers.Add(supplier);
        db.Users.Add(customer);
        db.Listings.Add(listing);
        db.Bookings.Add(booking);
        db.Orders.Add(order);
        db.PayoutEntries.Add(payout);
        await db.SaveChangesAsync();

        // Confirming fulfillment turns the provisioned (Accrued) payout into a real obligation.
        await MakeOrderService(db).ConfirmAsync(order.Id, customer.Id);

        (await db.Orders.FindAsync(order.Id))!.Status.Should().Be(OrderStatus.Confirmed);
        (await db.PayoutEntries.FirstAsync(p => p.OrderId == order.Id)).Status
            .Should().Be(PayoutStatus.Pending,
                "confirming the order must promote the Accrued payout to Pending under real Npgsql + FK enforcement");
    }
}
