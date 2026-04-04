using Ruumly.Backend.Data;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ruumly.Backend.Jobs;

public class StaleBookingCleanupJob(RuumlyDbContext db, ILogger<StaleBookingCleanupJob> logger)
{
    public async Task ExecuteAsync()
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);

        var staleBookings = await db.Bookings
            .Where(b => b.Status == BookingStatus.Pending
                     && b.CreatedAt < cutoff)
            .ToListAsync();

        foreach (var booking in staleBookings)
        {
            booking.Status    = BookingStatus.Cancelled;
            booking.UpdatedAt = DateTime.UtcNow;

            db.BookingTimelines.Add(new BookingTimeline
            {
                Id        = Guid.NewGuid(),
                BookingId = booking.Id,
                Event     = "Auto-cancelled: no payment within 24h",
                CreatedAt = DateTime.UtcNow,
            });

            logger.LogInformation("Auto-cancelled stale booking {Id}", booking.Id);
        }

        if (staleBookings.Count > 0)
            await db.SaveChangesAsync();

        logger.LogInformation("Stale booking cleanup: {Count} cancelled", staleBookings.Count);
    }
}
