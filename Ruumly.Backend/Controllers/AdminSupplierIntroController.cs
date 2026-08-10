using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

/// <summary>
/// The one-off supplier INTRODUCTION campaign. Ruumly's directory was built
/// from public research: ~562 providers across EE/LV/LT who have never heard of
/// us. This warms them before the first auto-fanout request lands cold in their
/// inbox and reads like spam.
///
/// It runs ONCE per supplier, ever. Two things enforce that:
/// <list type="bullet">
/// <item><c>dryRun</c> defaults to TRUE — sending is opt-in per request.</item>
/// <item><see cref="Supplier.IntroEmailSentAt"/> is stamped and committed
/// BEFORE any mail is queued, inside a Serializable transaction, so two
/// concurrent calls cannot both claim the same row.</item>
/// </list>
/// A duplicate send to 435 businesses is unrecoverable, so neither guard is
/// ever to be relaxed into an in-memory check.
/// </summary>
[Route("api/admin")]
public class AdminSupplierIntroController(
    RuumlyDbContext db,
    IBackgroundEmailQueue emailQueue,
    IMailDomainVerifier mailDomains,
    IConfiguration config,
    ILogger<AdminSupplierIntroController> logger) : AdminBaseController(db)
{
    // ── Pacing ────────────────────────────────────────────────────────────────
    // 20 recipients per batch, one batch every 5 minutes, and each email inside
    // a batch offset by 15 s (20 x 15 s = the full 5-minute window), so the
    // stream is a smooth drip with no burst even at a batch boundary.
    //
    // That is 4 emails/minute — 240/hour. Resend allows 2 requests/SECOND, so
    // we sit ~30x under the hard limit; the real constraint is the receiving
    // side. A domain that normally sends a handful of transactional mails a day
    // suddenly emitting 435 near-identical cold messages is the textbook
    // spam-filter trigger. 435 recipients spread this way take ~1 h 49 m.
    public const int BatchSize            = 20;
    public const int BatchIntervalMinutes = 5;
    public const int WithinBatchSeconds   = 15;

    /// <summary>Upper bound on <c>limit</c> — larger than the whole directory.</summary>
    private const int MaxLimit = 1000;

    // Skip reasons, as returned in the breakdown.
    private const string SkipNotFound      = "not_found";
    private const string SkipAlreadySent   = "already_sent";
    private const string SkipInactive      = "inactive";
    private const string SkipNotDirectory  = "not_directory";
    private const string SkipNoEmail       = "no_email";
    private const string SkipInvalidEmail  = "invalid_email";
    private const string SkipUndeliverable = "undeliverable_domain";
    private const string SkipDuplicate     = "duplicate_email";
    private const string SkipOverLimit     = "over_limit";

    /// <summary>
    /// Previews (default) or runs the introduction campaign.
    ///
    /// A dry run touches nothing: no email is queued, no <c>IntroEmailSentAt</c>
    /// is stamped, no audit row is written. It returns the match counts, the
    /// country/language breakdown, every skip reason with its count, the pacing
    /// schedule, the exact From/Reply-To, and the FULLY RENDERED subject, text
    /// body and HTML body for one real supplier per language. That response is
    /// the artefact the founder approves before a single email goes out.
    /// </summary>
    [HttpPost("suppliers/intro-campaign")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RunIntroCampaign(
        [FromBody] SupplierIntroCampaignRequest? body,
        CancellationToken ct)
    {
        var request = body ?? new SupplierIntroCampaignRequest();
        var dryRun  = request.IsDryRun;

        if (request.Limit is { } limit && (limit < 1 || limit > MaxLimit))
            return BadRequest(Error($"limit must be between 1 and {MaxLimit}."));

        var country = string.IsNullOrWhiteSpace(request.Country)
            ? null
            : request.Country.Trim().ToUpperInvariant();

        var appUrl   = config["AppUrl"];
        var from     = EmailFrom.Render(config);
        var opsInbox = await OpsInbox.ResolveAsync(Db);
        var replyTo  = opsInbox;

        IDbContextTransaction? transaction = null;
        try
        {
            // Live sends claim rows under Serializable so a double-click, a
            // retried request or a second admin cannot both send to the same
            // supplier. A dry run reads only and needs no isolation.
            if (!dryRun && Db.Database.IsRelational())
                transaction = await Db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            var candidates = await LoadCandidatesAsync(request, country, ct);

            // Deliverability is resolved HERE, while the recipient list is being
            // built, so a dead domain is reported as a skip in the preview the
            // founder approves rather than discovered as a bounce afterwards.
            // Only domains that survive the cheap checks are looked up.
            var undeliverableDomains = await mailDomains.FindUndeliverableAsync(
                DomainsToVerify(candidates, request), ct);

            var (eligible, skips) = Partition(candidates, request, undeliverableDomains);

            var messages = eligible
                .Select(s => (Supplier: s,
                              Message: SupplierIntroComposer.Compose(s, appUrl, opsInbox)))
                .ToList();

            var now    = DateTime.UtcNow;
            var pacing = BuildPacing(messages.Count, now);

            if (dryRun)
            {
                return Ok(BuildResponse(
                    dryRun: true, from, replyTo, candidates.Count, messages, skips, pacing,
                    sent: 0));
            }

            foreach (var (supplier, _) in messages)
                supplier.IntroEmailSentAt = now;

            Audit("supplier.intro_campaign_sent", User.GetUserEmail() ?? "admin",
                $"{messages.Count} suppliers",
                $"Sent: {messages.Count}, skipped: {skips.Values.Sum()} " +
                $"({string.Join(", ", skips.Select(kv => $"{kv.Key}={kv.Value}"))}). " +
                $"Country filter: {country ?? "all"}. From: {from}. Reply-To: {replyTo}. " +
                $"Paced {BatchSize}/{BatchIntervalMinutes}min, last send ~{pacing?.LastSendAt:u}.");

            // Persist and COMMIT before queueing: Hangfire writes jobs on its own
            // connection, so a failed save must never leave mail in flight that
            // no IntroEmailSentAt records. The reverse (stamped but not queued)
            // is the safe failure — a supplier simply never gets the intro.
            await Db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);

            for (var i = 0; i < messages.Count; i++)
            {
                var (supplier, message) = messages[i];
                emailQueue.EnqueueEmailAfter(
                    DelayFor(i), supplier.ContactEmail.Trim(),
                    message.Subject, message.TextBody, message.HtmlBody, replyTo);
            }

            logger.LogInformation(
                "Supplier intro campaign: queued {Count} email(s) from {From} (reply-to {ReplyTo}), " +
                "paced {BatchSize} per {Interval} min, last send ~{LastSendAt:u}.",
                messages.Count, from, replyTo, BatchSize, BatchIntervalMinutes, pacing?.LastSendAt);

            return Ok(BuildResponse(
                dryRun: false, from, replyTo, candidates.Count, messages, skips, pacing,
                sent: messages.Count));
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    // ─── Selection ────────────────────────────────────────────────────────────

    private async Task<List<Supplier>> LoadCandidatesAsync(
        SupplierIntroCampaignRequest request, string? country, CancellationToken ct)
    {
        var query = Db.Suppliers.AsQueryable();

        // An explicit id list is the founder testing on their own addresses
        // first; it overrides the country filter rather than intersecting it.
        if (request.SupplierIds is { Count: > 0 } ids)
        {
            var wanted = ids.Distinct().ToList();
            query = query.Where(s => wanted.Contains(s.Id));
        }
        else if (country is not null)
        {
            query = query.Where(s => s.Country.ToUpper() == country);
        }

        // Stable order so `limit` means the same set on the dry run that the
        // founder approved and on the live send that follows it.
        return await query.OrderBy(s => s.Name).ThenBy(s => s.Id).ToListAsync(ct);
    }

    /// <summary>
    /// The distinct email domains worth a DNS lookup: those belonging to a
    /// supplier who would otherwise be mailed. Mirrors the cheap checks in
    /// <see cref="Partition"/> so an inactive or already-mailed supplier never
    /// costs a resolver round-trip.
    /// </summary>
    private static List<string> DomainsToVerify(
        IReadOnlyList<Supplier> candidates, SupplierIntroCampaignRequest request)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var supplier in candidates)
        {
            if (supplier.IntroEmailSentAt is not null) continue;
            if (!supplier.IsActive)                    continue;
            if (!supplier.IsDirectoryListing && !request.IncludeNonDirectory) continue;

            if (EmailValidation.DomainOf(supplier.ContactEmail) is { } domain)
                domains.Add(domain);
        }

        return domains.ToList();
    }

    /// <summary>
    /// Splits candidates into who gets mail and why everyone else does not.
    /// Skip order is deliberate: <c>already_sent</c> is checked first so a
    /// second run reports the true reason rather than an incidental one, and
    /// <c>undeliverable_domain</c> precedes <c>duplicate_email</c> so two
    /// suppliers sharing one dead domain both report the real problem.
    /// </summary>
    private static (List<Supplier> Eligible, Dictionary<string, int> Skips) Partition(
        IReadOnlyList<Supplier> candidates,
        SupplierIntroCampaignRequest request,
        IReadOnlySet<string> undeliverableDomains)
    {
        var eligible = new List<Supplier>();
        var skips    = new Dictionary<string, int>(StringComparer.Ordinal);
        // Scraped directory rows share addresses (franchises, one info@ for
        // several brands). Two emails into one inbox is exactly the damage this
        // campaign is trying to avoid, so an address is used at most once.
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Skip(string reason) =>
            skips[reason] = skips.GetValueOrDefault(reason) + 1;

        if (request.SupplierIds is { Count: > 0 } ids)
        {
            var found = candidates.Select(s => s.Id).ToHashSet();
            var missing = ids.Distinct().Count(id => !found.Contains(id));
            if (missing > 0) skips[SkipNotFound] = missing;
        }

        foreach (var supplier in candidates)
        {
            if (supplier.IntroEmailSentAt is not null)          { Skip(SkipAlreadySent);  continue; }
            if (!supplier.IsActive)                             { Skip(SkipInactive);     continue; }
            if (!supplier.IsDirectoryListing && !request.IncludeNonDirectory)
                                                                { Skip(SkipNotDirectory); continue; }

            var email = supplier.ContactEmail?.Trim() ?? "";
            if (email.Length == 0)                              { Skip(SkipNoEmail);      continue; }
            if (!EmailValidation.IsValid(email))                { Skip(SkipInvalidEmail); continue; }

            // A syntactically perfect address on a domain with no mail exchanger
            // bounces. Bounces cost sender reputation, and this campaign is the
            // largest send this domain has ever made.
            var domain = EmailValidation.DomainOf(email);
            if (domain is null || undeliverableDomains.Contains(domain))
                                                                { Skip(SkipUndeliverable); continue; }

            if (!seenEmails.Add(email))                         { Skip(SkipDuplicate);    continue; }

            if (request.Limit is { } limit && eligible.Count >= limit)
            {
                Skip(SkipOverLimit);
                continue;
            }

            eligible.Add(supplier);
        }

        return (eligible, skips);
    }

    // ─── Pacing ───────────────────────────────────────────────────────────────

    /// <summary>Delay before recipient <paramref name="index"/> (0-based) is sent.</summary>
    public static TimeSpan DelayFor(int index) =>
        TimeSpan.FromMinutes(index / BatchSize * BatchIntervalMinutes)
      + TimeSpan.FromSeconds(index % BatchSize * WithinBatchSeconds);

    private static SupplierIntroPacingDto? BuildPacing(int count, DateTime now)
    {
        if (count == 0) return null;

        var last = DelayFor(count - 1);
        return new SupplierIntroPacingDto(
            BatchSize:            BatchSize,
            BatchIntervalMinutes: BatchIntervalMinutes,
            Batches:              (count + BatchSize - 1) / BatchSize,
            EmailsPerMinute:      Math.Round((double)BatchSize / BatchIntervalMinutes, 2),
            SpanMinutes:          (int)Math.Round(last.TotalMinutes),
            FirstSendAt:          now,
            LastSendAt:           now.Add(last));
    }

    // ─── Response ─────────────────────────────────────────────────────────────

    private static SupplierIntroCampaignResponse BuildResponse(
        bool dryRun,
        string from,
        string replyTo,
        int matched,
        IReadOnlyList<(Supplier Supplier, SupplierIntroMessage Message)> messages,
        Dictionary<string, int> skips,
        SupplierIntroPacingDto? pacing,
        int sent)
    {
        var byCountry = messages
            .GroupBy(m => string.IsNullOrWhiteSpace(m.Supplier.Country)
                ? "??" : m.Supplier.Country.ToUpperInvariant())
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var byLanguage = messages
            .GroupBy(m => m.Message.Language)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        // One fully rendered email per language — the copy the founder reviews.
        var samples = messages
            .GroupBy(m => m.Message.Language)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select(m => new SupplierIntroSampleDto(
                Language:     m.Message.Language,
                SupplierId:   m.Supplier.Id,
                SupplierName: m.Supplier.Name,
                Country:      m.Supplier.Country,
                To:           m.Supplier.ContactEmail.Trim(),
                Subject:      m.Message.Subject,
                TextBody:     m.Message.TextBody,
                HtmlBody:     m.Message.HtmlBody))
            .ToList();

        return new SupplierIntroCampaignResponse(
            DryRun:       dryRun,
            From:         from,
            ReplyTo:      replyTo,
            Matched:      matched,
            WouldSend:    messages.Count,
            Sent:         sent,
            ByCountry:    byCountry,
            ByLanguage:   byLanguage,
            SkippedTotal: skips.Values.Sum(),
            Skipped:      skips.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                               .Select(kv => new SupplierIntroSkipDto(kv.Key, kv.Value)).ToList(),
            Pacing:       pacing,
            Samples:      samples);
    }
}
