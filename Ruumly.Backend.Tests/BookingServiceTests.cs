using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

public class BookingServiceTests
{
    // ─── Test infrastructure ───────────────────────────────────────────────

    private static RuumlyDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<RuumlyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RuumlyDbContext(opts);
    }

    private static IConfiguration MakeConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppUrl"] = "https://test.ruumly.eu",
            })
            .Build();

    private static BookingService MakeService(RuumlyDbContext db) =>
        new(db,
            new NoOpOrderRoutingService(),
            new NoOpEmailSender(),
            NullLogger<BookingService>.Instance,
            MakeConfig(),
            new NoOpHttpContextAccessor());

    private sealed class NoOpOrderRoutingService : IOrderRoutingService
    {
        public Task RouteOrderAsync(Booking booking, Listing listing) => Task.CompletedTask;
    }

    private sealed class NoOpEmailSender : IEmailSender
    {
        public Task SendAsync(string to, string subject, string textBody, string? htmlBody = null)
            => Task.CompletedTask;
    }

    private sealed class NoOpHttpContextAccessor : Microsoft.AspNetCore.Http.IHttpContextAccessor
    {
        public Microsoft.AspNetCore.Http.HttpContext? HttpContext { get; set; }
    }

    // ─── Seed helpers ──────────────────────────────────────────────────────

    private static async Task<(Supplier supplier, Listing listing, User user)> SeedBasicAsync(
        RuumlyDbContext db,
        decimal priceFrom       = 100m,
        bool    isActive        = true,
        decimal clientDiscount  = 0m)
    {
        var supplier = new Supplier
        {
            Id                = Guid.NewGuid(),
            Name              = "TestSupplier",
            RegistryCode      = "123456",
            ContactName       = "Contact",
            ContactEmail      = "supplier@test.ee",
            ContactPhone      = "+372 5000 0000",
            ClientDiscountRate = clientDiscount,
        };

        var listing = new Listing
        {
            Id           = Guid.NewGuid(),
            SupplierId   = supplier.Id,
            Type         = ListingType.Warehouse,
            Title        = "Test Warehouse",
            Address      = "Test St 1",
            City         = "Tallinn",
            PriceFrom    = priceFrom,
            PriceUnit    = "kuu",
            IsActive     = isActive,
            AvailableNow = true,
            Description  = "Test",
        };

        var user = new User
        {
            Id    = Guid.NewGuid(),
            Email = "customer@test.ee",
            Name  = "Customer",
            Role  = UserRole.Customer,
        };

        db.Suppliers.Add(supplier);
        db.Listings.Add(listing);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (supplier, listing, user);
    }

    private static CreateBookingRequest MakeBookingRequest(
        Guid listingId,
        string startDate = "2026-05-01",
        string? endDate  = "2026-06-01",
        List<string>? extras = null) =>
        new()
        {
            ListingId    = listingId,
            StartDate    = startDate,
            EndDate      = endDate,
            Duration     = "1 kuu",
            Extras       = extras ?? [],
            ContactName  = "Test Customer",
            ContactEmail = "customer@test.ee",
            ContactPhone = "+372 5000 0001",
        };

    // ─── Pricing tests ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_Calculates_PlatformPrice_As_95pct_Of_BasePrice()
    {
        var db = CreateDb();
        var (_, listing, user) = await SeedBasicAsync(db, priceFrom: 100m);
        var service = MakeService(db);

        var booking = await service.CreateAsync(
            MakeBookingRequest(listing.Id), user.Id);

        booking.BasePrice.Should().Be(100m);
        booking.PlatformPrice.Should().Be(95m);   // 100 * 0.95 = 95
        booking.ExtrasTotal.Should().Be(0m);
        booking.Total.Should().Be(95m);
    }

    [Fact]
    public async Task CreateAsync_Calculates_Extras_Totals_Correctly()
    {
        var db = CreateDb();
        var (_, listing, user) = await SeedBasicAsync(db, priceFrom: 100m);
        var service = MakeService(db);

        // packing=15, loading=20 → extrasTotal=35, total=95+35=130
        var booking = await service.CreateAsync(
            MakeBookingRequest(listing.Id, extras: ["packing", "loading"]),
            user.Id);

        booking.PlatformPrice.Should().Be(95m);
        booking.ExtrasTotal.Should().Be(35m);
        booking.Total.Should().Be(130m);
    }

    [Fact]
    public async Task CreateAsync_All_Known_Extras_Prices_Match()
    {
        var db = CreateDb();
        var (_, listing, user) = await SeedBasicAsync(db, priceFrom: 100m);
        var service = MakeService(db);

        var booking = await service.CreateAsync(
            MakeBookingRequest(listing.Id, extras: ["packing", "loading", "insurance", "forklift"]),
            user.Id);

        // packing=15, loading=20, insurance=10, forklift=25 → 70
        booking.ExtrasTotal.Should().Be(70m);
        booking.Total.Should().Be(165m);  // 95 + 70
    }

    [Fact]
    public async Task CreateAsync_Rejects_Inactive_Listing()
    {
        var db = CreateDb();
        var (_, listing, user) = await SeedBasicAsync(db, isActive: false);
        var service = MakeService(db);

        var act = async () => await service.CreateAsync(MakeBookingRequest(listing.Id), user.Id);

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*"); // NotFoundException with localised message
    }

    [Fact]
    public async Task CreateAsync_Rejects_Overlapping_Booking_When_At_Capacity()
    {
        var db = CreateDb();
        var (supplier, listing, user) = await SeedBasicAsync(db, priceFrom: 100m);
        // Single-unit listing — already has a confirmed booking in the overlap window
        db.Bookings.Add(new Booking
        {
            Id         = Guid.NewGuid(),
            UserId     = user.Id,
            ListingId  = listing.Id,
            SupplierId = supplier.Id,
            StartDate  = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate    = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            Duration   = "1 kuu",
            Status     = BookingStatus.Confirmed,
        });
        await db.SaveChangesAsync();
        var service = MakeService(db);

        // Second booking overlaps — should fail (quantity = 1 is the default)
        var act = async () => await service.CreateAsync(
            MakeBookingRequest(listing.Id, startDate: "2026-05-15", endDate: "2026-05-25"),
            user.Id);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ─── Role scoping tests ────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_Customer_Sees_Only_Own_Bookings()
    {
        var db = CreateDb();
        var (supplier, listing, userA) = await SeedBasicAsync(db);

        var userB = new User
        {
            Id    = Guid.NewGuid(),
            Email = "other@test.ee",
            Name  = "Other",
            Role  = UserRole.Customer,
        };
        db.Users.Add(userB);

        // Two bookings: one for A, one for B
        db.Bookings.AddRange(
            new Booking { Id = Guid.NewGuid(), UserId = userA.Id, ListingId = listing.Id, SupplierId = supplier.Id, StartDate = DateTime.UtcNow, Duration = "1d" },
            new Booking { Id = Guid.NewGuid(), UserId = userB.Id, ListingId = listing.Id, SupplierId = supplier.Id, StartDate = DateTime.UtcNow, Duration = "1d" });
        await db.SaveChangesAsync();

        var service = MakeService(db);

        var resultA = await service.GetAllAsync(userA.Id, UserRole.Customer);
        var resultB = await service.GetAllAsync(userB.Id, UserRole.Customer);

        resultA.Should().HaveCount(1)
            .And.OnlyContain(b => b.ListingId == listing.Id);
        resultB.Should().HaveCount(1);

        // UserA's result should not contain UserB's booking
        resultA.Select(b => b.Id).Should().NotIntersectWith(resultB.Select(b => b.Id));
    }

    [Fact]
    public async Task GetAllAsync_Provider_Sees_Only_Own_Supplier_Bookings()
    {
        var db = CreateDb();

        var suppA = new Supplier { Id = Guid.NewGuid(), Name = "A", RegistryCode = "A", ContactName = "A", ContactEmail = "a@a.ee", ContactPhone = "1" };
        var suppB = new Supplier { Id = Guid.NewGuid(), Name = "B", RegistryCode = "B", ContactName = "B", ContactEmail = "b@b.ee", ContactPhone = "2" };

        var listA = new Listing { Id = Guid.NewGuid(), SupplierId = suppA.Id, Type = ListingType.Warehouse, Title = "A", Address = "A", City = "A", PriceUnit = "kuu", Description = "A" };
        var listB = new Listing { Id = Guid.NewGuid(), SupplierId = suppB.Id, Type = ListingType.Warehouse, Title = "B", Address = "B", City = "B", PriceUnit = "kuu", Description = "B" };

        var customer  = new User { Id = Guid.NewGuid(), Email = "cust@test.ee",  Name = "Cust", Role = UserRole.Customer };
        var providerA = new User { Id = Guid.NewGuid(), Email = "provA@test.ee", Name = "ProvA", Role = UserRole.Provider, SupplierId = suppA.Id };

        db.Suppliers.AddRange(suppA, suppB);
        db.Listings.AddRange(listA, listB);
        db.Users.AddRange(customer, providerA);

        db.Bookings.AddRange(
            new Booking { Id = Guid.NewGuid(), UserId = customer.Id, ListingId = listA.Id, SupplierId = suppA.Id, StartDate = DateTime.UtcNow, Duration = "1d" },
            new Booking { Id = Guid.NewGuid(), UserId = customer.Id, ListingId = listB.Id, SupplierId = suppB.Id, StartDate = DateTime.UtcNow, Duration = "1d" });
        await db.SaveChangesAsync();

        var service = MakeService(db);

        var results = await service.GetAllAsync(providerA.Id, UserRole.Provider);

        results.Should().HaveCount(1);
        results.Single().Provider.Should().Be("A");
    }
}
