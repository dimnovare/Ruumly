using Hangfire;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Services.Implementations;

namespace Ruumly.Backend.Jobs;

/// <summary>
/// Queues partner-confirm letters so consent is re-checked AT SEND TIME.
/// A campaign may be scheduled days ahead (startAt) and is paced over hours;
/// a provider that opts out, is deactivated or bounces in between must not
/// get the letter. Raw pre-rendered mail jobs could not know that.
/// </summary>
public interface IPartnerConfirmLetterQueue
{
    void Schedule(TimeSpan delay, Guid supplierId, string to, PartnerConfirm.Message msg, string? replyTo);
}

public sealed class HangfirePartnerConfirmLetterQueue(IBackgroundJobClient jobs) : IPartnerConfirmLetterQueue
{
    public void Schedule(TimeSpan delay, Guid supplierId, string to, PartnerConfirm.Message msg, string? replyTo)
    {
        var (subject, text, html) = (msg.Subject, msg.TextBody, msg.HtmlBody);
        if (delay <= TimeSpan.Zero)
            jobs.Enqueue<PartnerConfirmLetterJob>(j => j.SendAsync(supplierId, to, subject, text, html, replyTo));
        else
            jobs.Schedule<PartnerConfirmLetterJob>(j => j.SendAsync(supplierId, to, subject, text, html, replyTo), delay);
    }
}

public class PartnerConfirmLetterJob(
    RuumlyDbContext db,
    BackgroundEmailService email,
    ILogger<PartnerConfirmLetterJob> logger)
{
    public async Task SendAsync(Guid supplierId, string to, string subject, string textBody, string htmlBody, string? replyTo)
    {
        var why = await SkipReasonAsync(db, supplierId, to);
        if (why is not null)
        {
            logger.LogInformation("Partner confirm letter to {To} not sent: {Reason}.", to, why);
            return;
        }
        await email.SendWithReplyToAsync(to, subject, textBody, htmlBody, replyTo);
    }

    /// <summary>Null when the letter may go out; otherwise why it must not.</summary>
    public static async Task<string?> SkipReasonAsync(RuumlyDbContext db, Guid supplierId, string to, CancellationToken ct = default)
    {
        var inbox = (to ?? "").Trim().ToLowerInvariant();
        var s = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == supplierId, ct);
        if (s is null) return "supplier gone";
        if (s.MarketingOptOutAt is not null) return "opted out";
        if (!s.IsActive) return "inactive";
        if (s.ContactEmailUnusable) return "inbox bounced";
        if (PartnerConfirm.InboxKey(s) != inbox) return "e-mail changed since scheduling";
        if (s.PartnerConfirmAnswer is not null) return "already answered";
        // Any branch that shares this inbox and opted out reserves the inbox.
        var reserved = await db.Suppliers.AsNoTracking()
            .AnyAsync(x => x.MarketingOptOutAt != null && x.ContactEmail.ToLower().Trim() == inbox, ct);
        return reserved ? "inbox opted out" : null;
    }
}
