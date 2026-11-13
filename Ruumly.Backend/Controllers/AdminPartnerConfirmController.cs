using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

public sealed record PartnerConfirmCampaignRequest(
    List<Guid>? SupplierIds, bool? DryRun = null, int? Limit = null)
{
    public bool IsDryRun => DryRun ?? true;
}

public sealed record PartnerConfirmAnswerRequest(string Answer, string? Note = null);

/// <summary>
/// Admin side of partner confirmation (see <see cref="PartnerConfirm"/>):
/// start the campaign for an explicit list of suppliers, record an answer that
/// arrived by e-mail reply, and see where every row stands.
///
/// Same guards as the intro campaign, for the same reasons: dry run by default,
/// stamps committed under Serializable BEFORE any mail is queued, one mail per
/// inbox, opt-outs skipped first, dead domains skipped before sending.
/// </summary>
[Route("api/admin/suppliers/partner-confirm")]
public class AdminPartnerConfirmController(
    RuumlyDbContext db,
    IBackgroundEmailQueue emailQueue,
    IMailDomainVerifier mailDomains,
    IConfiguration config,
    ILogger<AdminPartnerConfirmController> logger) : AdminBaseController(db)
{
    private const int MaxLimit = 1000;

    [HttpPost("campaign")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Campaign([FromBody] PartnerConfirmCampaignRequest? body, CancellationToken ct)
    {
        var request = body ?? new PartnerConfirmCampaignRequest(null);
        if (request.SupplierIds is not { Count: > 0 })
            return BadRequest(Error("supplierIds is required: this campaign never targets the whole directory."));
        if (request.Limit is { } l && (l < 1 || l > MaxLimit))
            return BadRequest(Error($"limit must be between 1 and {MaxLimit}."));

        var dryRun = request.IsDryRun;
        IDbContextTransaction? tx = null;
        try
        {
            if (!dryRun && Db.Database.IsRelational())
                tx = await Db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            var ids = request.SupplierIds.Distinct().ToList();
            var rows = await Db.Suppliers.Where(s => ids.Contains(s.Id))
                .OrderBy(s => s.Name).ThenBy(s => s.Id).ToListAsync(ct);

            var domains = rows.Where(s => s.MarketingOptOutAt is null && s.IsActive)
                .Select(s => EmailValidation.DomainOf(s.ContactEmail)).OfType<string>().Distinct().ToList();
            var dead = await mailDomains.FindUndeliverableAsync(domains, ct);

            var skips = new Dictionary<string, int>(StringComparer.Ordinal);
            void Skip(string r) => skips[r] = skips.GetValueOrDefault(r) + 1;
            if (ids.Count > rows.Count) skips["not_found"] = ids.Count - rows.Count;

            // inbox -> (primary, siblings)
            var groups = new Dictionary<string, List<Supplier>>(StringComparer.OrdinalIgnoreCase);
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in rows)
            {
                var inbox = PartnerConfirm.InboxKey(s);
                if (s.MarketingOptOutAt is not null) { reserved.Add(inbox); Skip("opted_out"); continue; }
                if (s.PartnerConfirmSentAt is not null) { reserved.Add(inbox); Skip("already_sent"); continue; }
                if (!s.IsActive) { Skip("inactive"); continue; }
                if (inbox.Length == 0) { Skip("no_email"); continue; }
                if (s.ContactEmailUnusable) { Skip("bounced_email"); continue; }
                if (!EmailValidation.IsValid(inbox)) { Skip("invalid_email"); continue; }
                if (EmailValidation.DomainOf(inbox) is not { } d || dead.Contains(d)) { Skip("undeliverable_domain"); continue; }
                if (reserved.Contains(inbox)) { Skip("inbox_already_handled"); continue; }
                if (groups.TryGetValue(inbox, out var g)) { g.Add(s); continue; }   // sibling: same letter
                if (request.Limit is { } lim && groups.Count >= lim) { Skip("over_limit"); continue; }
                groups[inbox] = [s];
            }

            var supplierIds = groups.Values.SelectMany(g => g).Select(s => s.Id).ToList();
            var requestCounts = await Db.ProviderOutreaches
                .Where(o => supplierIds.Contains(o.SupplierId))
                .GroupBy(o => o.SupplierId).Select(g => new { g.Key, N = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.N, ct);

            var apiUrl = PartnerConfirm.ApiUrl(config);
            var replyTo = await OpsInbox.ResolveAsync(Db);
            var now = DateTime.UtcNow;
            var letters = new List<(string To, Supplier Primary, PartnerConfirm.Message Msg)>();
            foreach (var (inbox, g) in groups)
            {
                foreach (var s in g) s.PartnerConfirmToken ??= PartnerConfirm.NewToken();
                var n = g.Sum(s => requestCounts.GetValueOrDefault(s.Id));
                letters.Add((inbox, g[0], PartnerConfirm.Compose(g[0], g[0].PartnerConfirmToken!, apiUrl, n)));
            }

            var samples = letters.GroupBy(x => x.Msg.Language).Select(x => x.First())
                .Select(x => new { x.Msg.Language, Supplier = x.Primary.Name, x.To, x.Msg.Subject, x.Msg.TextBody, x.Msg.HtmlBody })
                .ToList();
            var byLanguage = letters.GroupBy(x => x.Msg.Language).ToDictionary(x => x.Key, x => x.Count());

            if (dryRun)
                return Ok(new { dryRun, matched = rows.Count, wouldSend = letters.Count,
                                suppliersCovered = supplierIds.Count, byLanguage, skipped = skips, samples });

            foreach (var (inbox, g) in groups)
                foreach (var s in g)
                {
                    s.PartnerConfirmSentAt = now;
                    s.PartnerConfirmInbox = inbox.ToLowerInvariant();
                    s.UpdatedAt = now;
                }
            Audit("supplier.partner_confirm_campaign", User.GetUserEmail() ?? "admin", $"{letters.Count} inboxes",
                $"Letters: {letters.Count}, suppliers: {supplierIds.Count}, skipped: " +
                $"{string.Join(", ", skips.Select(kv => $"{kv.Key}={kv.Value}"))}.");
            await Db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);

            for (var i = 0; i < letters.Count; i++)
            {
                var (to, _, msg) = letters[i];
                emailQueue.EnqueueEmailAfter(AdminSupplierIntroController.DelayFor(i), to,
                    msg.Subject, msg.TextBody, msg.HtmlBody, replyTo);
            }
            logger.LogInformation("Partner confirm campaign: queued {Count} letter(s) for {Suppliers} supplier(s).",
                letters.Count, supplierIds.Count);

            return Ok(new { dryRun, matched = rows.Count, sent = letters.Count,
                            suppliersCovered = supplierIds.Count, byLanguage, skipped = skips, samples });
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }
    }

    /// <summary>Records an answer that came by e-mail reply ("JAH") or phone.</summary>
    [HttpPost("{id:guid}/answer")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Answer(Guid id, [FromBody] PartnerConfirmAnswerRequest body, CancellationToken ct)
    {
        var answer = PartnerConfirm.Normalize(body?.Answer);
        if (answer.Length == 0) return BadRequest(Error("answer must be yes, business or no."));
        var supplier = await Db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null) return NotFound(Error("Supplier not found."));

        var rows = await PartnerConfirm.RecordAsync(Db, supplier, answer,
            $"{User.GetUserEmail() ?? "admin"}{(string.IsNullOrWhiteSpace(body!.Note) ? "" : $" ({body.Note.Trim()})")}",
            DateTime.UtcNow, ct);
        await Db.SaveChangesAsync(ct);
        return Ok(rows.Select(s => new { s.Id, s.Name, s.IsActive, s.ServesConsumers, s.PartnerConfirmAnswer, s.MarketingOptOutAt }));
    }

    /// <summary>Where every row in the flow stands.</summary>
    [HttpGet("status")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var rows = await Db.Suppliers.AsNoTracking()
            .Where(s => s.PartnerConfirmSentAt != null)
            .OrderBy(s => s.PartnerConfirmSentAt).ThenBy(s => s.Name)
            .Select(s => new
            {
                s.Id, s.Name, s.Country, s.ContactEmail, s.IsActive, s.PartnerConfirmSentAt,
                s.PartnerConfirmReminderSentAt, s.PartnerConfirmAnswer, s.PartnerConfirmAnsweredAt,
                s.PartnerConfirmExpiredAt, s.MarketingOptOutAt,
            })
            .ToListAsync(ct);

        var staged = rows.Select(r => new
        {
            stage = r.PartnerConfirmAnswer is { } a ? $"answered_{a}"
                  : r.PartnerConfirmExpiredAt is not null ? "expired"
                  : r.PartnerConfirmReminderSentAt is not null ? "reminded"
                  : "waiting",
            row = r,
        }).ToList();

        var automation = await Db.PlatformSettings.Where(s => s.Key == PartnerConfirm.AutomationSetting)
            .Select(s => s.Value).FirstOrDefaultAsync(ct);
        return Ok(new
        {
            automation = string.Equals(automation?.Trim(), "on", StringComparison.OrdinalIgnoreCase) ? "on" : "off",
            counts = staged.GroupBy(r => r.stage).ToDictionary(g => g.Key, g => g.Count()),
            rows = staged,
            now,
        });
    }
}
