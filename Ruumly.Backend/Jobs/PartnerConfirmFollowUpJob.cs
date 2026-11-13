using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Jobs;

/// <summary>
/// Daily follow-up for partner confirmation (see <see cref="PartnerConfirm"/>).
/// Does NOTHING unless the <c>partnerConfirmAutomation</c> platform setting is
/// "on", so deploying it changes no data until the founder flips it.
///
/// Per run, in this order:
/// <list type="number">
/// <item>A provider that QUOTED on a customer request after the letter went out
///   has answered with its actions: recorded as "yes".</item>
/// <item>No answer 14 days after the first letter → one reminder per inbox,
///   capped per run (<c>partnerConfirmDailyCap</c>, default 80) so the Resend
///   daily quota is never blown; the rest wait for the next run. A bounced inbox
///   is stamped without sending: it cannot answer, and the clock still runs.</item>
/// <item>No answer 7 days after the reminder → the row is deactivated and
///   stamped <c>PartnerConfirmExpiredAt</c>. Not an opt-out: a later "yes" click
///   restores it.</item>
/// </list>
/// Opted-out rows are never mailed or changed. A summary goes to the ops inbox.
/// </summary>
public class PartnerConfirmFollowUpJob(
    RuumlyDbContext db,
    IBackgroundEmailQueue emailQueue,
    IConfiguration config,
    ILogger<PartnerConfirmFollowUpJob> logger)
{
    public sealed record Result(bool Enabled, int AutoYes, int Reminded, int ReminderSkippedDead, int Expired, int Deferred);

    public async Task<Result> ExecuteAsync() => await RunAsync(DateTime.UtcNow);

    public async Task<Result> RunAsync(DateTime now, CancellationToken ct = default)
    {
        var settings = await db.PlatformSettings
            .Where(s => s.Key == PartnerConfirm.AutomationSetting || s.Key == PartnerConfirm.DailyCapSetting)
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        if (!string.Equals(settings.GetValueOrDefault(PartnerConfirm.AutomationSetting)?.Trim(), "on",
                StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Partner confirm follow-up: automation is off, nothing done.");
            return new Result(false, 0, 0, 0, 0, 0);
        }
        var cap = int.TryParse(settings.GetValueOrDefault(PartnerConfirm.DailyCapSetting), out var c) && c >= 0
            ? c : PartnerConfirm.DefaultDailyCap;

        var pending = await db.Suppliers
            .Where(s => s.PartnerConfirmSentAt != null && s.PartnerConfirmAnswer == null
                     && s.PartnerConfirmExpiredAt == null && s.MarketingOptOutAt == null)
            .OrderBy(s => s.PartnerConfirmSentAt).ThenBy(s => s.Name)
            .ToListAsync(ct);

        // 1. Quoting is answering.
        var ids = pending.Select(s => s.Id).ToList();
        var quoted = await db.ProviderOutreaches
            .Where(o => ids.Contains(o.SupplierId) && o.QuotedAt != null)
            .Select(o => new { o.SupplierId, o.QuotedAt })
            .ToListAsync(ct);
        var autoYes = 0;
        foreach (var s in pending.ToList())
        {
            if (s.PartnerConfirmAnswer is not null) { pending.Remove(s); continue; }   // sibling already done
            if (!quoted.Any(q => q.SupplierId == s.Id && q.QuotedAt >= s.PartnerConfirmSentAt)) continue;
            var rows = await PartnerConfirm.RecordAsync(db, s, PartnerConfirm.Yes, "job (provider quoted)", now, ct);
            autoYes += rows.Count;
        }
        pending.RemoveAll(s => s.PartnerConfirmAnswer is not null);

        // 2. Reminders, one per inbox.
        var apiUrl = PartnerConfirm.ApiUrl(config);
        var replyTo = await OpsInbox.ResolveAsync(db);
        var dueReminder = pending
            .Where(s => s.PartnerConfirmReminderSentAt is null && s.IsActive
                     && s.PartnerConfirmSentAt <= now.AddDays(-PartnerConfirm.ReminderAfterDays))
            .GroupBy(PartnerConfirm.InboxKey)
            .ToList();
        var dueIds = dueReminder.SelectMany(g => g).Select(s => s.Id).ToList();
        var counts = await db.ProviderOutreaches.Where(o => dueIds.Contains(o.SupplierId))
            .GroupBy(o => o.SupplierId).Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct);

        int reminded = 0, dead = 0, deferred = 0;
        var queued = new List<(string To, PartnerConfirm.Message Msg)>();
        foreach (var group in dueReminder)
        {
            var primary = group.First();
            if (primary.ContactEmailUnusable || group.Key.Length == 0)
            {
                foreach (var s in group) s.PartnerConfirmReminderSentAt = now;
                dead++;
                continue;
            }
            if (queued.Count >= cap) { deferred++; continue; }
            primary.PartnerConfirmToken ??= PartnerConfirm.NewToken();
            var msg = PartnerConfirm.Compose(primary, primary.PartnerConfirmToken, apiUrl,
                group.Sum(s => counts.GetValueOrDefault(s.Id)), primary.PartnerConfirmSentAt);
            queued.Add((group.Key, msg));
            foreach (var s in group) s.PartnerConfirmReminderSentAt = now;
            reminded++;
        }

        // 3. Expire the silent.
        var expired = new List<Supplier>();
        // Only rows still active: a row an admin already deactivated must not be
        // stamped Expired, or a later "yes" would silently reverse the admin's call.
        foreach (var s in pending.Where(s => s.IsActive && s.PartnerConfirmReminderSentAt is not null
                     && s.PartnerConfirmReminderSentAt <= now.AddDays(-PartnerConfirm.ExpireAfterReminderDays)))
        {
            s.PartnerConfirmExpiredAt = now;
            s.IsActive = false;
            s.UpdatedAt = now;
            expired.Add(s);
            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(), Action = "supplier.partner_confirm_expired", Actor = "job",
                Target = s.Name, CreatedAt = now,
                Detail = $"No answer to the partner-confirm letter ({s.PartnerConfirmSentAt:u}) or the reminder " +
                         $"({s.PartnerConfirmReminderSentAt:u}). Deactivated; a later yes restores it.",
            });
        }

        // Stamps are committed before mail is queued (stamped-but-unsent is the safe failure).
        await db.SaveChangesAsync(ct);
        for (var i = 0; i < queued.Count; i++)
            emailQueue.EnqueueEmailAfter(AdminSupplierIntroController.DelayFor(i), queued[i].To,
                queued[i].Msg.Subject, queued[i].Msg.TextBody, queued[i].Msg.HtmlBody, replyTo);

        var result = new Result(true, autoYes, reminded, dead, expired.Count, deferred);
        logger.LogInformation("Partner confirm follow-up: {@Result}", result);

        if (autoYes + reminded + dead + expired.Count + deferred > 0)
        {
            var body = $"Partner confirmation, {now:yyyy-MM-dd}\n\n" +
                       $"Answered yes by quoting: {autoYes}\nReminders sent: {reminded}\n" +
                       $"Reminder skipped (bounced inbox): {dead}\nDeferred to tomorrow (cap {cap}): {deferred}\n" +
                       $"Deactivated for no answer: {expired.Count}\n" +
                       (expired.Count > 0 ? "\n" + string.Join("\n", expired.Select(s => $"- {s.Name} ({s.Country}, {s.ContactEmail})")) + "\n" : "");
            emailQueue.EnqueueEmail(replyTo, $"Partner confirmation: {reminded} reminders, {expired.Count} deactivated", body);
        }
        return result;
    }
}
