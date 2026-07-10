using System.Security.Cryptography;
using System.Text;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

/// <summary>
/// Executes transactional email sends inside a Hangfire-owned dependency scope.
/// Failures are rethrown so Hangfire can retry and retain the failure history.
/// </summary>
public class BackgroundEmailService(
    RuumlyDbContext db,
    IEmailSender emailSender,
    IConfiguration config,
    ILogger<BackgroundEmailService> logger)
{
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 300, 600])]
    public async Task SendAsync(string to, string subject, string textBody, string? htmlBody = null)
    {
        await emailSender.SendAsync(to, subject, textBody, htmlBody);
        logger.LogInformation("Background email sent to {Email} with subject {Subject}", to, subject);
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 300, 600])]
    public async Task SendWithReplyToAsync(string to, string subject, string textBody, string? htmlBody, string? replyTo)
    {
        await emailSender.SendAsync(to, subject, textBody, htmlBody, replyTo);
        logger.LogInformation("Background email sent to {Email} with subject {Subject}", to, subject);
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 300, 600])]
    public async Task SendVerificationEmailAsync(Guid userId)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
        {
            logger.LogWarning("Verification email skipped because user {UserId} was not found", userId);
            return;
        }

        if (user.EmailVerified)
        {
            logger.LogInformation("Verification email skipped because user {UserId} is already verified", userId);
            return;
        }

        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var tokenBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        user.EmailVerificationToken = Convert.ToHexString(tokenBytes).ToLowerInvariant();
        user.EmailVerificationExpiry = DateTime.UtcNow.AddHours(24);
        await db.SaveChangesAsync();

        var verifyUrl = FrontendUrl.Localized(
            config["AppUrl"], user.Language, $"verify?token={rawToken}");
        var t = EmailTranslations.For(user.Language);
        var body =
            $"{t.EmailVerifyGreeting}\n\n" +
            $"{t.EmailVerifyBody}\n\n" +
            $"{verifyUrl}\n\n" +
            $"{t.EmailVerifyExpiry}\n\n" +
            "Ruumly\ninfo@ruumly.eu";

        await emailSender.SendAsync(user.Email, t.EmailVerifySubject, body);
        logger.LogInformation("Background verification email sent to user {UserId}", userId);
    }
}
