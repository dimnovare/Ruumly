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

    [Fact]
    public async Task Signed_But_Unpaid_Booking_Is_Cancelled_And_Contract_Voided()
    {
        var db     = TestDbContext.Create();
        var logger = new SpyLogger();

        var booking = NewStaleBooking(hoursOld: 48);
        var contract = new SignedContract
        {
            Id        = Guid.NewGuid(),
            BookingId = booking.Id,
            Status    = "completed",   // signed via Dokobit, never paid
            TenantName = "Mari",
            TenantEmail = "mari@test.ee",
        };

        db.Bookings.Add(booking);
        db.SignedContracts.Add(contract);
        await db.SaveChangesAsync();

        var job = new StaleBookingCleanupJob(db, logger);

        await job.ExecuteAsync();

        var updatedBooking  = await db.Bookings.FindAsync(booking.Id);
        var updatedContract = await db.SignedContracts.FindAsync(contract.Id);

        updatedBooking!.Status.Should().Be(BookingStatus.Cancelled);
        updatedContract!.Status.Should().Be("void", "a signed-but-unpaid contract binds no one");

        // Timeline event records the void
        var timeline = db.BookingTimelines.Where(t => t.BookingId == booking.Id).ToList();
        timeline.Should().Contain(t => t.Event.Contains("contract voided"));
    }

    [Fact]
    public async Task Configurable_Window_Is_Honored_3h_Old_Booking_Cancelled_When_Window_Is_2h()
    {
        var db     = TestDbContext.Create();
        var logger = new SpyLogger();

        // Window = 2h; booking is 3h old → should be cancelled.
        db.PlatformSettings.Add(new PlatformSetting
        {
            Key = StaleBookingCleanupJob.ExpiryHoursSettingKey,
            Value = "2",
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = "test",
        });
        var booking = NewStaleBooking(hoursOld: 3);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        await new StaleBookingCleanupJob(db, logger).ExecuteAsync();

        var updated = await db.Bookings.FindAsync(booking.Id);
        updated!.Status.Should().Be(BookingStatus.Cancelled,
            "a 3h-old booking exceeds the configured 2h window");
    }

    [Fact]
    public async Task Configurable_Window_Is_Honored_3h_Old_Booking_Kept_When_Default_24h()
    {
        var db     = TestDbContext.Create();
        var logger = new SpyLogger();

        // No PlatformSetting → fallback 24h; a 3h-old booking is well within and stays Pending.
        var booking = NewStaleBooking(hoursOld: 3);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        await new StaleBookingCleanupJob(db, logger).ExecuteAsync();

        var updated = await db.Bookings.FindAsync(booking.Id);
        updated!.Status.Should().Be(BookingStatus.Pending,
            "within the default 24h window the booking must not be cancelled");
    }

    private static Booking NewStaleBooking(int hoursOld) => new()
    {
        Id         = Guid.NewGuid(),
        UserId     = Guid.NewGuid(),
        ListingId  = Guid.NewGuid(),
        SupplierId = Guid.NewGuid(),
        StartDate  = DateTime.UtcNow.AddDays(5),
        Duration   = "1 kuu",
        Status     = BookingStatus.Pending,
        CreatedAt  = DateTime.UtcNow.AddHours(-hoursOld),
    };
}
