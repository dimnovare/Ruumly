using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Ruumly.Backend.Constants;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Responses;
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

    // ─── How far to look, per service ─────────────────────────────────────────
    //
    // One 25 → 50 → 100 km ladder used to serve every category, which cannot be
    // right for all of them, because the two halves of the catalogue have
    // OPPOSITE geography:
    //
    //   • Storage is a fixed place the CUSTOMER drives to. Nobody drives 100 km
    //     to visit their own boxes, so a wide search there is not a wider net —
    //     it is a wrong answer that also burns a cold contact with a provider who
    //     was never a real candidate.
    //   • Movers, van and trailer rental TRAVEL TO the customer, and intercity
    //     work is normal, so the wide ladder is right for them.
    //   • Cleaning crews work a metro area rather than a country: wider than
    //     storage, tighter than a mover.
    //
    // Founder decision, 2026-08-14. Every ladder still starts tight and widens
    // only when the quota is unfilled — the change is what "tight" and "wide"
    // mean for each service, not the widening behaviour.
    //
    // Three steps in every ladder so the widening loop stays a simple index; see
    // RadiusFor.
    private const int AutoRadiusSteps = 3;

    private static readonly double[] StorageRadiiKm  = [15d, 25d,  40d];
    private static readonly double[] TravelRadiiKm   = [25d, 50d, 100d];
    private static readonly double[] CleaningRadiiKm = [20d, 35d,  50d];

    /// <summary>How far to search for this service at widening step <paramref name="step"/>.</summary>
    private static double RadiusFor(DemandLeadCategory category, int step) =>
        (category switch
        {
            DemandLeadCategory.Warehouse => StorageRadiiKm,
            DemandLeadCategory.Cleaning  => CleaningRadiiKm,
            _                            => TravelRadiiKm,
        })[step];

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
        // The directory is imported one row per BRANCH, so several supplier rows
        // routinely stand behind a single head-office inbox. Deduping ids alone
        // therefore mails one company twice about one request, and each copy
        // carries its OWN quote token — so the same business can answer the same
        // customer with two competing prices. Same reasoning, same shape as
        // AdminSupplierIntroController's campaign dedupe.
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Reserving the address on the way out is the difference between deduping
        // ROWS and deduping INBOXES: without it a sibling branch inherits the
        // freed slot and delivers the very copy this row was refused.
        void Reserve(Supplier s)
        {
            if (s.ContactEmail.Trim() is { Length: > 0 } address) seenEmails.Add(address);
        }

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
                    Reserve(supplier);
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
                    Reserve(supplier);
                    skipped.Add(new(supplierId, supplier.Name, "email_bounced"));
                    continue;
                }
                if (!resend && contactedSupplierIds.Contains(supplierId))
                {
                    Reserve(supplier);
                    skipped.Add(new(supplierId, supplier.Name, "already_contacted"));
                    continue;
                }

                var to = supplier.ContactEmail.Trim();
                // Checked last, so a recipient we refuse for a reason of its own
                // reports that reason rather than this catch-all. `resend` does
                // not override it: an admin asking to re-contact a company still
                // means one letter, not one per branch row they ticked.
                if (!seenEmails.Add(to))
                {
                    skipped.Add(new(supplierId, supplier.Name, "duplicate_email"));
                    continue;
                }

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
        // What we will ask providers to price. Category "any" used to end the
        // fan-out here, because it conflates two very different requests: one we
        // cannot describe, and one that simply names more services than a single
        // Category column can hold. The intake's own copy invites the second
        // ("pick everything you need"), so the customers who followed our
        // instructions were exactly the ones nobody was contacted for.
        var searchCategories = ResolveSearchCategories(lead);

        // Nothing concrete survived — zero routable services (an insurance-only
        // ask, say). The original reasoning stands for this case and only this
        // case: ProviderCandidateFinder matches EVERY supplier for an Any lead, so
        // searching now would blast the whole directory and burn the one cold
        // contact we get with each of them, for a request we cannot even state.
        // Alert the admin and let them route it by hand.
        if (searchCategories.Count == 0)
            return AutoOutreachSummary.Skipped("category_any");

        if (!await GetBoolSettingAsync("conciergeAutoOutreach", DefaultAutoOutreach, ct))
            return AutoOutreachSummary.Skipped("disabled");

        var max = await GetIntSettingAsync(
            "conciergeAutoOutreachMax", DefaultAutoOutreachMax,
            MinAutoOutreachMax, MaxAutoOutreachMax, ct);

        // One search per (service × place). A move has TWO places — see
        // ResolveSearchAnchors.
        var anchors = ResolveSearchAnchors(lead, searchCategories);

        var picked         = new List<Guid>();
        var noEmail        = 0;
        var pickedRadiusKm = MaxRadiusAt(anchors, 0);
        var searchedKm     = pickedRadiusKm;

        // Widen only as far as needed to fill the quota: a Tallinn provider is a
        // better answer than a Tartu one, and the finder already ranks
        // exact-city first, then by distance.
        //
        // The loop walks a widening STEP rather than a shared kilometre value,
        // because each service now has its own ladder: step 0 of a storage +
        // moving request searches 15 km for the storage and 25 km for the
        // movers, simultaneously and correctly.
        for (var step = 0; step < AutoRadiusSteps; step++)
        {
            searchedKm = MaxRadiusAt(anchors, step);
            var ranked = new List<IReadOnlyList<ProviderCandidateDto>>();
            foreach (var anchor in anchors)
            {
                var matches = await ProviderCandidateFinder.SearchAsync(
                    // Searched one service at a time, never as the Any wildcard:
                    // the finder treats Any as "every supplier matches", which is
                    // the blast itself. Each provider we pick therefore actually
                    // does one of the services the customer asked for.
                    db, AsSearch(lead, anchor),
                    new ProviderCandidateSearch(
                        Query: null, AllEstonia: false, AllCategories: false,
                        RadiusKm: RadiusFor(anchor.Category, step), Limit: 50),
                    ct);
                ranked.Add(matches.Items);
            }

            var candidates   = new List<Guid>();
            var missingEmail = 0;
            // Rebuilt per radius, like `candidates` itself: each widening starts
            // the pick over from the top of the ranking.
            var seenEmails   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in MergeByService(ranked))
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
                // MergeByService dedupes supplier IDs, which is not the same as
                // deduping companies: the directory holds one row per branch and
                // the branches share an inbox. Dropped HERE rather than left for
                // SendAsync to refuse, so the slot passes to the next distinct
                // company instead of shrinking the fan-out — a request answered
                // by one provider twice and a competitor not at all is the worst
                // of both. Not counted as unreachable: the company IS contacted,
                // through the higher-ranked row.
                if (!seenEmails.Add(candidate.ContactEmail.Trim())) continue;
                candidates.Add(candidate.SupplierId);
            }

            // Only adopt a wider radius when it actually reaches somebody new,
            // so the ops alert never claims a 100 km search for a provider that
            // was next door.
            if (candidates.Count > picked.Count || picked.Count == 0)
            {
                picked         = candidates;
                noEmail        = missingEmail;
                pickedRadiusKm = searchedKm;
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

    // ─── Which services a lead is fanned out on ───────────────────────────────

    /// <summary>
    /// The services to look for providers of. A concrete category speaks for
    /// itself; an Any lead is a concierge request whose selection did not fit the
    /// single Category column, so the pick is recovered from the Query machine
    /// summary the intake wrote (see <see cref="ServiceCategories.SelectedSlugs"/>).
    ///
    /// Recovered rather than stored in a new column on purpose: the intake already
    /// persists the full selection there, so nothing about this needs a migration
    /// or a backfill — the leads sitting un-contacted in production today can be
    /// worked by the same code path.
    ///
    /// Empty means the request named nothing we can route, which is the one case
    /// that must NOT fan out.
    /// </summary>
    private static List<DemandLeadCategory> ResolveSearchCategories(DemandLead lead) =>
        lead.Category != DemandLeadCategory.Any
            ? [lead.Category]
            : ServiceCategories.SelectedSlugs(lead.Query)
                .Select(slug => ServiceCategories.BySlug[slug])
                .ToList();

    /// <summary>One search: a service, looked for around one city.</summary>
    private sealed record SearchAnchor(DemandLeadCategory Category, string City);

    /// <summary>
    /// Every (service × place) the fan-out should search.
    ///
    /// A move has TWO relevant places and only the origin was ever searched, so a
    /// Tallinn → Tartu request never reached a single Tartu mover — often the
    /// cheaper half of that market, and the half most motivated to take a job
    /// that ends at their own door. The destination lived on the lead the whole
    /// time (<see cref="DemandLead.ToCity"/>); the search simply dropped it.
    ///
    /// Only MOVING gains the second anchor. Storage, cleaning, van and trailer
    /// rental are all consumed at the origin — a customer moving to Tartu still
    /// picks up the van in Tallinn — so searching the destination for those would
    /// cold-email businesses that cannot serve the request at all.
    ///
    /// Deduped case-insensitively: "Tallinn → tallinn" is one place, and
    /// searching it twice would let one city's providers take both halves of a
    /// quota meant to be shared.
    /// </summary>
    private static List<SearchAnchor> ResolveSearchAnchors(
        DemandLead lead, IReadOnlyList<DemandLeadCategory> categories)
    {
        var anchors = new List<SearchAnchor>();
        var seen    = new HashSet<(DemandLeadCategory, string)>();

        void Add(DemandLeadCategory category, string? city)
        {
            if (string.IsNullOrWhiteSpace(city)) return;
            var trimmed = city.Trim();
            if (seen.Add((category, trimmed.ToLowerInvariant())))
                anchors.Add(new SearchAnchor(category, trimmed));
        }

        foreach (var category in categories)
        {
            Add(category, lead.City);
            if (category == DemandLeadCategory.Moving)
                Add(category, lead.ToCity);
        }

        return anchors;
    }

    /// <summary>
    /// The widest radius any anchor searches at this widening step — what the ops
    /// alert reports, so "no reachable provider within N km" names the furthest we
    /// actually looked rather than the tightest ladder in the mix.
    /// </summary>
    private static double MaxRadiusAt(IReadOnlyList<SearchAnchor> anchors, int step) =>
        anchors.Count == 0 ? 0 : anchors.Max(a => RadiusFor(a.Category, step));

    /// <summary>
    /// The lead as the finder should see it for ONE anchor. A detached copy, not
    /// a mutation: `lead` is tracked by the same DbContext the outreach then saves
    /// through, so steering a search by assigning its Category or City would
    /// persist the wrong request on the customer's own row — and for the
    /// destination anchor that would silently rewrite where they said they live.
    /// Carries only what the finder reads: the id (its already-contacted map),
    /// the city being searched, and the category.
    /// </summary>
    private static DemandLead AsSearch(DemandLead lead, SearchAnchor anchor) =>
        new() { Id = lead.Id, City = anchor.City, Category = anchor.Category };

    /// <summary>
    /// Flattens the per-anchor candidate lists into one, taking the best
    /// remaining provider from each anchor in turn.
    ///
    /// Round-robin rather than concatenation because the quota is shared: a
    /// "moving + warehouse" request whose six slots all went to the movers the
    /// first search returned would leave the storage half of that customer's move
    /// unquoted, which is the same silent failure as not fanning out at all. The
    /// same reasoning now covers the two ENDS of a move: without round-robin the
    /// origin city would take every slot and the destination movers we just
    /// started searching for would never actually be mailed.
    ///
    /// A provider offering two of the requested services — or sitting in both
    /// cities' catchments — is emailed once; the dedupe here is what keeps that
    /// from costing them two cold contacts.
    /// </summary>
    private static IEnumerable<ProviderCandidateDto> MergeByService(
        IReadOnlyList<IReadOnlyList<ProviderCandidateDto>> perService)
    {
        var seen  = new HashSet<Guid>();
        var depth = perService.Count == 0 ? 0 : perService.Max(list => list.Count);

        for (var rank = 0; rank < depth; rank++)
        {
            foreach (var list in perService)
            {
                if (rank >= list.Count) continue;
                var candidate = list[rank];
                if (seen.Add(candidate.SupplierId))
                    yield return candidate;
            }
        }
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
