using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

public class ReservationExpiryTests
{
    private sealed class FakeEmailSender : IEmailSender
    {
        public List<(string To, string Subject)> Sent { get; } = [];
        public Task SendAsync(string to, string subject, string textBody, string? htmlBody = null)
        {
            Sent.Add((to, subject));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ExpireReservationsAsync_Cancels_Expired_Reserved_Booking()
    {
        // Arrange
        var db       = TestDbContext.Create();
        var email    = new FakeEmailSender();
        var config   = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppUrl"] = "https://ruumly.eu" })
            .Build();

        var supplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = "Test OÜ", ContactName = "T",
            ContactEmail = "t@t.ee", ContactPhone = "1",
        };
        var listing = new Listing
        {
            Id = Guid.NewGuid(), SupplierId = supplier.Id, Type = ListingType.Warehouse,
            Title = "Warehouse A", Address = "Addr", City = "Tallinn",
            PriceFrom = 50, PriceUnit = "kuu", IsActive = true,
        };
        var booking = new Booking
        {
            Id            = Guid.NewGuid(),
            UserId        = Guid.NewGuid(),
            ListingId     = listing.Id,
            SupplierId    = supplier.Id,
            StartDate     = DateTime.UtcNow.AddDays(1),
            EndDate       = DateTime.UtcNow.AddDays(30),
            Duration      = "1 kuu",
            Status        = BookingStatus.Reserved,
            ReservedUntil = DateTime.UtcNow.AddHours(-1), // expired 1 hour ago
            ContactName   = "Test User",
            ContactEmail  = "user@test.ee",
        };

        db.Suppliers.Add(supplier);
        db.Listings.Add(listing);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var service = new BackgroundCleanupService(
            db, email, config,
            NullLogger<BackgroundCleanupService>.Instance);

        // Act
        await service.ExpireReservationsAsync();

        // Assert — status changed to Cancelled
        var updated = await db.Bookings.FindAsync(booking.Id);
        updated!.Status.Should().Be(BookingStatus.Cancelled);

        // Assert — timeline event recorded
        var timeline = await db.BookingTimelines
            .Where(t => t.BookingId == booking.Id)
            .ToListAsync();
        timeline.Should().ContainSingle()
            .Which.Event.Should().Be("reservation-expired");

        // Assert — expiry email sent
        email.Sent.Should().ContainSingle()
            .Which.To.Should().Be("user@test.ee");
    }

    [Fact]
    public async Task ExpireReservationsAsync_Does_Not_Cancel_Active_Reservation()
    {
        // Arrange — reservation still valid (expires in 12 hours)
        var db       = TestDbContext.Create();
        var email    = new FakeEmailSender();
        var config   = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppUrl"] = "https://ruumly.eu" })
            .Build();

        var supplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = "Test OÜ", ContactName = "T",
            ContactEmail = "t@t.ee", ContactPhone = "1",
        };
        var booking = new Booking
        {
            Id            = Guid.NewGuid(),
            UserId        = Guid.NewGuid(),
            ListingId     = Guid.NewGuid(),
            SupplierId    = supplier.Id,
            StartDate     = DateTime.UtcNow.AddDays(1),
            Duration      = "1 kuu",
            Status        = BookingStatus.Reserved,
            ReservedUntil = DateTime.UtcNow.AddHours(12), // still valid
            ContactName   = "Test User",
            ContactEmail  = "user@test.ee",
        };

        db.Suppliers.Add(supplier);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var service = new BackgroundCleanupService(
            db, email, config,
            NullLogger<BackgroundCleanupService>.Instance);

        // Act
        await service.ExpireReservationsAsync();

        // Assert — still Reserved
        var updated = await db.Bookings.FindAsync(booking.Id);
        updated!.Status.Should().Be(BookingStatus.Reserved);
        email.Sent.Should().BeEmpty();
    }
}
