using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Jobs;

public class FoundingPartnerExpiryReminderJob(
    RuumlyDbContext db,
    INotificationService notificationService,
    IEmailSender emailSender,
    IConfiguration config,
    ILogger<FoundingPartnerExpiryReminderJob> logger)
{
    public async Task ExecuteAsync()
    {
        var now       = DateTime.UtcNow;
        var windowMin = now.AddDays(29);
        var windowMax = now.AddDays(31);
        var recentCutoff = now.AddDays(-25);

        var suppliers = await db.Suppliers
            .Where(s => s.FoundingPartner
                     && s.FoundingPartnerUntil.HasValue
                     && s.FoundingPartnerUntil.Value >= windowMin
                     && s.FoundingPartnerUntil.Value <= windowMax
                     && (!s.FoundingPartnerReminderSentAt.HasValue
                         || s.FoundingPartnerReminderSentAt.Value < recentCutoff))
            .ToListAsync();

        if (suppliers.Count == 0)
        {
            logger.LogInformation("FoundingPartnerExpiryReminder: no suppliers to notify");
            return;
        }

        var appUrl = config["AppUrl"] ?? "https://ruumly.eu";

        foreach (var supplier in suppliers)
        {
            var expiryDate = supplier.FoundingPartnerUntil!.Value;
            var dateStr    = expiryDate.ToString("dd.MM.yyyy");

            // Find provider users linked to this supplier
            var users = await db.Users
                .Where(u => u.SupplierId == supplier.Id && u.Role == UserRole.Provider)
                .ToListAsync();

            foreach (var user in users)
            {
                var lang = user.Language ?? "et";
                var isEt = lang == "et";

                var title = isEt
                    ? $"Sinu asutajapartneri staatus lõppeb {dateStr}"
                    : $"Your Founding Partner status ends {dateStr}";

                var desc = isEt
                    ? $"Täname, et olete olnud Ruumly asutajapartner! "
                      + $"30 päeva pärast ({dateStr}) lähete üle tavahinnastikule. "
                      + "Vaadake oma plaani üle /provider lehel või vastake sellele kirjale pikendamise arutamiseks."
                    : $"Thank you for being a Ruumly Founding Partner! "
                      + $"In 30 days ({dateStr}) you will transition to standard pricing. "
                      + "Review your plan on the /provider page or reply to this email to discuss renewal.";

                // In-app notification
                await notificationService.CreateAsync(
                    user.Id,
                    NotificationType.System,
                    title,
                    desc,
                    actionUrl: "/provider");

                // Email notification
                if (!string.IsNullOrWhiteSpace(user.Email))
                {
                    var emailBody = $"{desc}\n\n{appUrl}/provider\n\nRuumly\ninfo@ruumly.eu";

                    try
                    {
                        await emailSender.SendAsync(user.Email, title, emailBody);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex,
                            "Failed to send founding partner expiry email to {Email} for supplier {SupplierId}",
                            user.Email, supplier.Id);
                    }
                }
            }

            supplier.FoundingPartnerReminderSentAt = now;
            supplier.UpdatedAt = now;

            logger.LogInformation(
                "Sent founding partner expiry reminder for supplier {SupplierId} ({Name}), expires {ExpiryDate}",
                supplier.Id, supplier.Name, dateStr);
        }

        await db.SaveChangesAsync();

        logger.LogInformation("FoundingPartnerExpiryReminder: notified {Count} supplier(s)", suppliers.Count);
    }
}
