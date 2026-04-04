using Ruumly.Backend.Data;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ruumly.Backend.Jobs;

public class AbandonedBookingReminderJob(
    RuumlyDbContext db,
    IEmailSender emailSender,
    IConfiguration config,
    ILogger<AbandonedBookingReminderJob> logger)
{
    public async Task ExecuteAsync()
    {
        var cutoff1h = DateTime.UtcNow.AddHours(-1);
        var cutoff2h = DateTime.UtcNow.AddHours(-2);

        // Find bookings created 1-2h ago that are still pending
        var abandoned = await db.Bookings
            .Include(b => b.Listing)
            .Where(b => b.Status == BookingStatus.Pending
                     && b.CreatedAt < cutoff1h
                     && b.CreatedAt > cutoff2h
                     && !string.IsNullOrEmpty(b.ContactEmail))
            .ToListAsync();

        foreach (var booking in abandoned)
        {
            var user = await db.Users.FindAsync(booking.UserId);
            var lang = user?.Language ?? "et";
            var t    = EmailTranslations.For(lang);
            var bookingUrl = $"{config["AppUrl"]}/account?tab=bookings";

            var subject = $"Ruumly: {t.AbandonedSubject}";
            var body    = $"{t.AbandonedGreeting} {booking.ContactName},\n\n" +
                          $"{t.AbandonedBody}\n\n" +
                          $"{t.AbandonedService}: {booking.Listing?.Title}\n" +
                          $"{t.AbandonedTotal}: €{booking.Total:F2}\n\n" +
                          $"{t.AbandonedCta}: {bookingUrl}\n\n" +
                          $"Ruumly\ninfo@ruumly.eu";

            await emailSender.SendAsync(booking.ContactEmail!, subject, body);
            logger.LogInformation("Abandoned booking reminder sent to {Email} for {Id}",
                booking.ContactEmail, booking.Id);
        }

        logger.LogInformation("Abandoned booking check: {Count} reminders sent", abandoned.Count);
    }
}
