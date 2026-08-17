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
    IStorageService storageService,
    ILogger<BackgroundCleanupService> logger)
{
    /// <summary>
    /// How long a customer's request photos live. Long enough for the whole
    /// concierge loop — outreach, a slow provider's quote, the customer
    /// choosing, a follow-up — and not a day longer. They are pictures of
    /// somebody's home; keeping them past their usefulness is the kind of thing
    /// that is nobody's problem right up until it is.
    /// </summary>
    public const int LeadPhotoRetentionDays = 30;

    /// <summary>
    /// Deletes request photos older than <see cref="LeadPhotoRetentionDays"/>
    /// from the private bucket and clears the keys off the lead.
    ///
    /// Also collects ORPHANS by construction: an upload that was never submitted
    /// with a request has no lead pointing at it, so nothing here clears it —
    /// which is why the sweep below deletes by AGE OF THE LEAD and a separate
    /// bucket lifecycle rule should cover uploads that never became a lead at
    /// all. See the note in LeadPhotoController: the anonymous endpoint can be
    /// made to store bytes that are never claimed.
    ///
    /// The lead itself is kept. A request that went nowhere is data — the
    /// audit trail, the metrics denominators and the "what were we missing"
    /// question all depend on it. Only the photos expire.
    ///
    /// Registered as a Hangfire recurring job (daily).
    /// </summary>
    public async Task CleanupLeadPhotosAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-LeadPhotoRetentionDays);
        var expired = await db.DemandLeads
            .Where(l => l.PhotoKeysJson != null && l.CreatedAt < cutoff)
            .ToListAsync();
        if (expired.Count == 0) return;

        var storage = storageService;
        var deleted = 0;

        foreach (var lead in expired)
        {
            foreach (var key in LeadPhotos.Keys(lead.PhotoKeysJson))
            {
                try
                {
                    await storage.DeletePrivateAsync(key);
                    deleted++;
                }
                catch (Exception ex)
                {
                    // A single object that will not delete must not strand every
                    // other lead's photos behind it. Logged and skipped; the next
                    // run tries again because the keys stay on the lead until the
                    // whole set succeeds.
                    logger.LogWarning(ex, "Could not delete expired lead photo {Key}.", key);
                }
            }
            lead.PhotoKeysJson = null;
        }

        await db.SaveChangesAsync();
        logger.LogInformation(
            "Lead photo cleanup: cleared {Leads} lead(s), deleted {Objects} object(s) older than {Days} days.",
            expired.Count, deleted, LeadPhotoRetentionDays);
    }

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
