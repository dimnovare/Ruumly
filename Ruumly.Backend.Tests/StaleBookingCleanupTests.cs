using FluentAssertions;
using Microsoft.Extensions.Logging;
using Ruumly.Backend.Jobs;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Tests;

public class StaleBookingCleanupTests
{
    private sealed class SpyLogger : ILogger<StaleBookingCleanupJob>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public async Task Does_Not_Cancel_Pending_Booking_With_Paid_Invoice()
    {
        var db     = TestDbContext.Create();
        var logger = new SpyLogger();

        var supplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = "Test OÜ", ContactName = "T",
            ContactEmail = "t@t.ee", ContactPhone = "1",
        };
        var booking = new Booking
        {
            Id         = Guid.NewGuid(),
            UserId     = Guid.NewGuid(),
            ListingId  = Guid.NewGuid(),
            SupplierId = supplier.Id,
            StartDate  = DateTime.UtcNow.AddDays(5),
            Duration   = "1 kuu",
            Status     = BookingStatus.Pending,
            CreatedAt  = DateTime.UtcNow.AddHours(-48), // stale: 48h old
        };
        var invoice = new Invoice
        {
            Id        = Guid.NewGuid(),
            BookingId = booking.Id,
            Amount    = 100m,
            Status    = InvoiceStatus.Paid,
            PaidAt    = DateTime.UtcNow.AddHours(-47),
        };

        db.Suppliers.Add(supplier);
        db.Bookings.Add(booking);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var job = new StaleBookingCleanupJob(db, logger);

        // Act
        await job.ExecuteAsync();

        // Assert — booking still Pending (not cancelled)
        var updated = await db.Bookings.FindAsync(booking.Id);
        updated!.Status.Should().Be(BookingStatus.Pending);

        // Assert — warning was logged with booking and supplier IDs
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning
            && e.Message.Contains(booking.Id.ToString())
            && e.Message.Contains(supplier.Id.ToString()));
    }

    [Fact]
    public async Task Cancels_Pending_Booking_Without_Paid_Invoice()
    {
        var db     = TestDbContext.Create();
        var logger = new SpyLogger();

        var supplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = "Test OÜ", ContactName = "T",
            ContactEmail = "t@t.ee", ContactPhone = "1",
        };
        var booking = new Booking
        {
            Id         = Guid.NewGuid(),
            UserId     = Guid.NewGuid(),
            ListingId  = Guid.NewGuid(),
            SupplierId = supplier.Id,
            StartDate  = DateTime.UtcNow.AddDays(5),
            Duration   = "1 kuu",
            Status     = BookingStatus.Pending,
            CreatedAt  = DateTime.UtcNow.AddHours(-48),
        };

        db.Suppliers.Add(supplier);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var job = new StaleBookingCleanupJob(db, logger);

        // Act
        await job.ExecuteAsync();

        // Assert — booking cancelled
        var updated = await db.Bookings.FindAsync(booking.Id);
        updated!.Status.Should().Be(BookingStatus.Cancelled);
    }
}
