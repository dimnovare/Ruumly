using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

/// <summary>
/// The one place that emails providers about a demand lead. Extracted from
/// AdminOffersController so the admin batch and the automatic fan-out on
/// concierge intake share exactly one implementation of the delicate parts:
/// the Serializable transaction, the already-contacted dedupe, per-recipient
/// quote-token minting, the New → Contacted lifecycle move, and the rule that
/// emails are enqueued only after the transaction commits.
/// </summary>
public sealed class ConciergeOutreachService(
    RuumlyDbContext db,
    IBackgroundEmailQueue emailQueue,
    IConfiguration config,
    ILogger<ConciergeOutreachService> logger) : IConciergeOutreachService
{
    // Reply-To on provider correspondence — matches the info@ address printed in
    // the email signature (EmailTranslations). NOT the ops alert destination
    // (Helpers/OpsInbox); it's where a provider's reply must land, so it stays
    // paired with the signature text.
    private const string OpsReplyTo = OpsInbox.Fallback;

    /// <summary>Audit actor for machine-initiated fan-out (a human admin's id is used otherwise).</summary>
    public const string AutoActor = "auto-outreach";

    // Start tight, widen only if we cannot fill the fan-out quota. Tallinn/
    // Harjumaa first: 25 km is the same default the admin workspace uses.
    private static readonly double[] AutoRadiiKm = [25d, 50d, 100d];

    private const bool DefaultAutoOutreach    = true;
    private const int  DefaultAutoOutreachMax = 6;
    private const int  MinAutoOutreachMax     = 1;
    private const int  MaxAutoOutreachMax     = 12;

    public async Task<OutreachSendResult> SendAsync(
        DemandLead lead,
        IReadOnlyList<Guid> supplierIds,
        bool resend,
        string actor,
        CancellationToken ct = default)
    {
        var requestedIds = supplierIds.Distinct().ToList();
        if (requestedIds.Count == 0) return OutreachSendResult.Empty;

        var suppliers = await db.Suppliers
            .Where(s => requestedIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        var sent    = new List<OutreachSentRecipient>();
        var skipped = new List<OutreachSkippedRecipient>();
        var emails  = new List<(string To, string Subject, string TextBody, string? HtmlBody)>();
        IDbContextTransaction? transaction = null;

        try
        {
            if (db.Database.IsRelational())
                transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            var contactedSupplierIds = (await db.ProviderOutreaches
                    .Where(o => o.DemandLeadId == lead.Id && requestedIds.Contains(o.SupplierId))
                    .Select(o => o.SupplierId)
                    .Distinct()
                    .ToListAsync(ct))
                .ToHashSet();

            foreach (var supplierId in requestedIds)
            {
                if (!suppliers.TryGetValue(supplierId, out var supplier))
                {
                    skipped.Add(new(supplierId, null, "not_found"));
                    continue;
                }
                // Checked before everything else: this one is a promise we made in
                // writing, not an operational condition. The finder already
                // excludes opted-out suppliers, so reaching here means an admin
                // asked for this supplier explicitly — and the answer is still no.
                if (supplier.MarketingOptOutAt is not null)
                {
                    skipped.Add(new(supplierId, supplier.Name, "opted_out"));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(supplier.ContactEmail))
                {
                    skipped.Add(new(supplierId, supplier.Name, "no_email"));
                    continue;
                }
                // A hard bounce / spam complaint retired this address (Resend
                // webhook). Sending again cannot reach anyone and only damages
                // our sending reputation — ops must fix the address first, which
                // clears the flag. Not overridable by `resend`.
                if (supplier.ContactEmailUnusable)
                {
                    skipped.Add(new(supplierId, supplier.Name, "email_bounced"));
                    continue;
                }
                if (!resend && contactedSupplierIds.Contains(supplierId))
                {
                    skipped.Add(new(supplierId, supplier.Name, "already_contacted"));
                    continue;
                }

                var to = supplier.ContactEmail.Trim();
                // Per-recipient quote token: the provider opens /{lang}/quote/{token}
                // and submits a price without an account. Minted here so the link in
                // the email and the stored row always carry the same token.
                var quoteToken = OfferToken.Generate();
                var message = ProviderOutreachComposer.Compose(
                    lead, supplier, config["AppUrl"], quoteToken);
                emails.Add((to, message.Subject, message.TextBody, message.HtmlBody));

                var row = new ProviderOutreach
                {
                    Id           = Guid.NewGuid(),
                    DemandLeadId = lead.Id,
                    SupplierId   = supplier.Id,
                    SentTo       = to,
                    SentAt       = DateTime.UtcNow,
                    Status       = ProviderOutreachStatus.Sent,
                    QuoteToken   = quoteToken,
                };
                db.ProviderOutreaches.Add(row);
                sent.Add(new(row, supplier.Name));
            }

            // First outreach is the first touch: New → Contacted (stamps
            // ContactedAt). Leads further down the funnel keep their status.
            if (emails.Count > 0 && lead.Status == DemandLeadStatus.New)
                DemandLeadLifecycle.MoveTo(lead, DemandLeadStatus.Contacted);

            db.AuditLogs.Add(new AuditLog
            {
                Id        = Guid.NewGuid(),
                Action    = "lead.outreach_sent",
                Actor     = actor,
                Target    = lead.Id.ToString(),
                Detail    = $"Sent: {sent.Count}, skipped: {skipped.Count}",
                CreatedAt = DateTime.UtcNow,
            });

            // Save and commit BEFORE enqueueing: Hangfire commits jobs on its own
            // connection, so failed persistence must never double-email providers.
            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
        }
        catch (Exception ex) when (IsSerializationFailure(ex))
        {
            if (transaction is not null)
            {
                try { await transaction.RollbackAsync(ct); }
                catch { /* A failed commit may already have closed the transaction. */ }
            }

            return OutreachSendResult.Conflict;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }

        foreach (var (to, subject, textBody, htmlBody) in emails)
            emailQueue.EnqueueEmail(to, subject, textBody, htmlBody, OpsReplyTo);

        return new(sent, skipped, false);
    }

    public async Task<AutoOutreachSummary> AutoFanOutAsync(
        DemandLead lead, CancellationToken ct = default)
    {
        // Category "any" means the visitor picked zero or several services. We
        // have nothing specific to ask a provider to price, and a vague blast
        // burns the one cold-contact we get with each of them — alert the admin
        // instead and let them route it by hand.
        if (lead.Category == DemandLeadCategory.Any)
            return AutoOutreachSummary.Skipped("category_any");

        if (!await GetBoolSettingAsync("conciergeAutoOutreach", DefaultAutoOutreach, ct))
            return AutoOutreachSummary.Skipped("disabled");

        var max = await GetIntSettingAsync(
            "conciergeAutoOutreachMax", DefaultAutoOutreachMax,
            MinAutoOutreachMax, MaxAutoOutreachMax, ct);

        var picked         = new List<Guid>();
        var noEmail        = 0;
        var pickedRadiusKm = AutoRadiiKm[0];
        var searchedKm     = AutoRadiiKm[0];

        // Widen only as far as needed to fill the quota: a Tallinn provider is a
        // better answer than a Tartu one, and the finder already ranks
        // exact-city first, then by distance.
        foreach (var candidateRadius in AutoRadiiKm)
        {
            searchedKm = candidateRadius;
            var matches = await ProviderCandidateFinder.SearchAsync(
                db, lead,
                new ProviderCandidateSearch(
                    Query: null, AllEstonia: false, AllCategories: false,
                    RadiusKm: candidateRadius, Limit: 50),
                ct);

            var candidates   = new List<Guid>();
            var missingEmail = 0;
            foreach (var candidate in matches.Items)
            {
                if (candidates.Count >= max) break;
                // The finder only returns active suppliers; an unreachable
                // address is the remaining reason we cannot contact one —
                // either never captured, or proven dead by a bounce. Both count
                // as "no email" for the ops alert, and both make the fan-out
                // move on to the next candidate instead of burning a slot.
                if (string.IsNullOrWhiteSpace(candidate.ContactEmail) || candidate.ContactEmailUnusable)
                {
                    missingEmail++;
                    continue;
                }
                candidates.Add(candidate.SupplierId);
            }

            // Only adopt a wider radius when it actually reaches somebody new,
            // so the ops alert never claims a 100 km search for a provider that
            // was next door.
            if (candidates.Count > picked.Count || picked.Count == 0)
            {
                picked         = candidates;
                noEmail        = missingEmail;
                pickedRadiusKm = candidateRadius;
            }

            if (picked.Count >= max) break;
        }

        if (picked.Count == 0)
            return AutoOutreachSummary.Skipped("no_candidates", noEmail, searchedKm);

        var result = await SendAsync(lead, picked, resend: false, actor: AutoActor, ct);
        if (result.SerializationConflict)
        {
            logger.LogWarning(
                "Auto-outreach for concierge lead {LeadId} lost a serialization race; not retried.",
                lead.Id);
            return AutoOutreachSummary.Skipped("conflict", noEmail, pickedRadiusKm);
        }

        var names = result.Sent
            .Select(s => s.SupplierName ?? s.Row.SentTo)
            .ToList();
        logger.LogInformation(
            "Auto-outreach for concierge lead {LeadId}: emailed {Sent} provider(s) within {RadiusKm} km ({NoEmail} had no email).",
            lead.Id, names.Count, pickedRadiusKm, noEmail);

        return new(
            names.Count,
            noEmail + result.Skipped.Count(s => s.Reason is "no_email" or "email_bounced"),
            pickedRadiusKm,
            null,
            names);
    }

    // ─── PlatformSettings (key/value table — no migration for new keys) ───────

    private async Task<string?> GetSettingAsync(string key, CancellationToken ct) =>
        await db.PlatformSettings
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

    private async Task<bool> GetBoolSettingAsync(string key, bool fallback, CancellationToken ct)
    {
        var raw = (await GetSettingAsync(key, ct))?.Trim();
        return string.IsNullOrEmpty(raw) ? fallback : raw.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<int> GetIntSettingAsync(
        string key, int fallback, int min, int max, CancellationToken ct)
    {
        var raw = (await GetSettingAsync(key, ct))?.Trim();
        var value = int.TryParse(raw, out var parsed) ? parsed : fallback;
        return Math.Clamp(value, min, max);
    }

    private static bool IsSerializationFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure })
                return true;
        }

        return false;
    }
}
