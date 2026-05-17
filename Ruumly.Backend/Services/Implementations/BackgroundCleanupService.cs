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
            .Include(b => b.Order)
            .Where(b => b.Status == BookingStatus.Reserved
                     && b.ReservedUntil.HasValue
                     && b.ReservedUntil.Value < now)
            .ToListAsync();

        if (expired.Count == 0) return;

        // Batch-load users to avoid N+1 queries
        var userIds = expired.Select(b => b.UserId).Distinct().ToList();
        var users   = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        foreach (var booking in expired)
        {
            booking.Status    = BookingStatus.Cancelled;
            booking.UpdatedAt = now;

            if (booking.Order is not null && booking.Order.Status != OrderStatus.Cancelled)
            {
                booking.Order.Status    = OrderStatus.Cancelled;
                booking.Order.UpdatedAt = now;
            }

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
                var lang         = users.GetValueOrDefault(booking.UserId)?.Language ?? "et";
                var t            = EmailTranslations.For(lang);
                var bookAgainUrl = $"{config["AppUrl"]}/listings";
                var listingTitle = booking.Listing?.Title ?? "your listing";

                var subject = $"Ruumly: {t.ReservationExpiredSubject}";
                var body    = $"{t.ReservationExpiredGreeting.Replace("{name}", booking.ContactName)}\n\n"
                            + $"{t.ReservationExpiredBody.Replace("{listing}", listingTitle)}\n\n"
                            + $"{t.ReservationExpiredCta}: {bookAgainUrl}\n\n"
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
