using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class BackgroundCleanupService(
    RuumlyDbContext db,
    IEmailSender emailSender,
    IConfiguration config,
    ILogger<BackgroundCleanupService> logger)
{
    /// <summary>
    /// Deletes refresh tokens that are revoked or expired, provided they were
    /// created more than 7 days ago (grace period to avoid racing active sessions).
    /// Registered as a Hangfire recurring job (daily).
    /// </summary>
    public async Task CleanupStaleRefreshTokensAsync()
    {
        var ageCutoff = DateTime.UtcNow.AddDays(-7);
        var now       = DateTime.UtcNow;

        var deleted = await db.RefreshTokens
            .Where(t => (t.IsRevoked || t.ExpiresAt < now) && t.CreatedAt < ageCutoff)
            .ExecuteDeleteAsync();

        logger.LogInformation(
            "CleanupStaleRefreshTokens: deleted {Count} stale refresh token(s)", deleted);
    }

    /// <summary>
    /// Cancels Reserved bookings whose hold window has expired.
    /// Registered as a Hangfire recurring job (every 15 minutes).
    /// </summary>
    public async Task ExpireReservationsAsync()
    {
        var now = DateTime.UtcNow;

        var expired = await db.Bookings
            .Include(b => b.Listing)
            .Where(b => b.Status == BookingStatus.Reserved
                     && b.ReservedUntil.HasValue
                     && b.ReservedUntil.Value < now)
            .ToListAsync();

        if (expired.Count == 0) return;

        foreach (var booking in expired)
        {
            booking.Status    = BookingStatus.Cancelled;
            booking.UpdatedAt = now;

            db.BookingTimelines.Add(new BookingTimeline
            {
                Id        = Guid.NewGuid(),
                BookingId = booking.Id,
                Event     = "reservation-expired",
                Status    = BookingStatus.Cancelled,
                CreatedAt = now,
            });

            // Send expiry notification email
            if (!string.IsNullOrWhiteSpace(booking.ContactEmail))
            {
                var bookingUser = await db.Users.FindAsync(booking.UserId);
                var lang        = bookingUser?.Language ?? "et";
                var t           = EmailTranslations.For(lang);
                var bookAgainUrl = $"{config["AppUrl"]}/listings";
                var listingTitle = booking.Listing?.Title ?? "your listing";

                var subject = lang == "et"
                    ? $"Broneering #{booking.Id.ToString()[..8].ToUpper()} aegus"
                    : $"Reservation #{booking.Id.ToString()[..8].ToUpper()} expired";

                var body = lang == "et"
                    ? $"Tere {booking.ContactName},\n\n"
                      + $"Teie broneering teenusele \"{listingTitle}\" aegus, "
                      + "kuna makset ei laekunud 24 tunni jooksul.\n\n"
                      + $"Broneerige uuesti: {bookAgainUrl}\n\n"
                      + "Ruumly\ninfo@ruumly.eu"
                    : $"Hi {booking.ContactName},\n\n"
                      + $"Your reservation for \"{listingTitle}\" has expired "
                      + "because payment was not received within 24 hours.\n\n"
                      + $"Book again: {bookAgainUrl}\n\n"
                      + "Ruumly\ninfo@ruumly.eu";

                try
                {
                    await emailSender.SendAsync(booking.ContactEmail, subject, body);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Failed to send reservation expiry email for booking {BookingId}",
                        booking.Id);
                }
            }
        }

        await db.SaveChangesAsync();

        logger.LogInformation(
            "ExpireReservations: cancelled {Count} expired reservation(s)", expired.Count);
    }

    /// <summary>
    /// Deletes PollingLog entries older than 90 days.
    /// Keeps the table from growing unbounded at high polling frequency.
    /// </summary>
    public async Task PruneOldPollingLogsAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-90);
        var deleted = await db.PollingLogs
            .Where(p => p.Timestamp < cutoff)
            .ExecuteDeleteAsync();

        logger.LogInformation(
            "PruneOldPollingLogs: deleted {Count} log(s) older than 90 days", deleted);
    }
}
