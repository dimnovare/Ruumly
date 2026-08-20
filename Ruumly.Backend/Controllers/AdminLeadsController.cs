using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Constants;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

[Route("api/admin")]
public class AdminLeadsController(
    RuumlyDbContext db,
    IBackgroundEmailQueue emailQueue) : AdminBaseController(db)
{
    /// <summary>
    /// How long a request may sit with no provider answer before the queue calls
    /// it stalled.
    ///
    /// A policy number, so it lives in ONE place and is echoed to the client as
    /// <c>stalledAfterDays</c> rather than re-declared there — a queue whose
    /// count and whose row badges disagree about the threshold is worse than no
    /// badge at all. Three days is the founder's own chase cadence: shorter and
    /// every request is "stalled" the morning after it arrives, longer and the
    /// Viljandi case (18 asks, no reply of any kind, noticed weeks later) stays
    /// invisible for exactly as long as it did.
    /// </summary>
    private const int StalledAfterDays = 3;

    /// <summary>
    /// Statuses that mean the PROVIDER did something. NoAnswer is deliberately
    /// not among them: it is an admin recording silence, not a provider breaking
    /// it, and counting it as an answer would erase the very thing this data
    /// exists to make visible.
    /// </summary>
    private static bool IsProviderAnswer(ProviderOutreachStatus status) =>
        status is ProviderOutreachStatus.Replied
               or ProviderOutreachStatus.Declined
               or ProviderOutreachStatus.NeedsInfo;

    /// <summary>The mail provably did not reach a human (Resend webhook only).</summary>
    private static bool IsDeliveryFailure(ProviderOutreachStatus status) =>
        status is ProviderOutreachStatus.Bounced or ProviderOutreachStatus.Complained;

    /// <summary>
    /// Admin lead queue. All filters are optional and case-insensitive
    /// (null/blank = ignore): <paramref name="status"/>, <paramref name="source"/>
    /// (e.g. "concierge" for the demand funnel, "routed"/"notify-interest" for the
    /// rest), <paramref name="category"/> (a ServiceCategories slug or "any"), and
    /// <paramref name="city"/>. Default order is newest-first; set
    /// <paramref name="needsResponse"/>=true to get the SLA view — only untouched
    /// New leads (ContactedAt == null), oldest-first, so the oldest un-worked
    /// request is at the top of the queue.
    ///
    /// Beside <c>items</c> the response carries two things the row shape itself
    /// cannot hold (see <see cref="AdminMappers.MapAdminLead"/>, which is shared
    /// and deliberately unchanged):
    ///
    ///   • <c>outreach</c> — per-lead provider-side summary, keyed by lead id.
    ///     Without it "has anyone answered this request?" costs one expand per
    ///     row, which at 11 requests a week is 11 clicks to learn that the answer
    ///     is no. It is a sibling map rather than extra fields on each item so
    ///     the item shape the frontend already reads stays byte-for-byte intact.
    ///   • <c>queues</c> — honest counts for the three views an operator actually
    ///     works from, computed over the SAME filters but over the whole result
    ///     set rather than the current page, so a chip can carry a number without
    ///     lying about what it counted.
    ///
    /// <paramref name="queue"/> selects one of those three views and orders it
    /// oldest-first, because in all three the oldest row is the one that has been
    /// waiting longest. <paramref name="needsResponse"/> is the older boolean
    /// spelling of <c>queue=needsresponse</c>, kept because the admin cockpit and
    /// the alert emails link with <c>?needsResponse=1</c>.
    /// </summary>
    /// <param name="queue">needsresponse | blocked | stalled (unknown = ignored).</param>
    [HttpGet("leads")]
    public async Task<IActionResult> GetLeads(
        [FromQuery] string? status = null,
        [FromQuery] string? source = null,
        [FromQuery] string? category = null,
        [FromQuery] string? city = null,
        [FromQuery] bool needsResponse = false,
        [FromQuery] string? queue = null,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50)
    {
        page  = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);

        var query = Db.DemandLeads.AsQueryable();

        if (!string.IsNullOrEmpty(status) &&
            Enum.TryParse<DemandLeadStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(d => d.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            // ToLower (not ILike) so the same expression runs on Npgsql and the
            // InMemory test provider.
            var src = source.Trim().ToLower();
            query = query.Where(d => d.Source != null && d.Source.ToLower() == src);
        }

        // Category is an enum column — resolve the slug ("any" or a
        // ServiceCategories slug) to the enum. An unknown slug is ignored (no
        // filter), matching the null=ignore contract of the other params.
        if (!string.IsNullOrWhiteSpace(category))
        {
            var slug = category.Trim().ToLowerInvariant();
            DemandLeadCategory? cat = slug == "any"
                ? DemandLeadCategory.Any
                : ServiceCategories.BySlug.TryGetValue(slug, out var c) ? c : null;
            if (cat is { } cc)
                query = query.Where(d => d.Category == cc);
        }

        if (!string.IsNullOrWhiteSpace(city))
        {
            var cityLower = city.Trim().ToLower();
            query = query.Where(d => d.City.ToLower() == cityLower);
        }

        // Snapshot BEFORE the queue selection is applied: the chips' counts must
        // describe the same filtered world the operator is looking at, minus the
        // queue they happen to have selected — otherwise "Needs first reply (3)"
        // reads 3 only while it is already on.
        var filtered = query;

        // An unknown queue name is ignored rather than rejected — same null=ignore
        // contract as every other filter here, so a stale bookmark shows the full
        // list instead of a 400 the operator has to decode mid-loop.
        var selectedQueue = queue?.Trim().ToLowerInvariant();
        if (needsResponse && selectedQueue is null) selectedQueue = "needsresponse";
        var stalledBefore = DateTime.UtcNow.AddDays(-StalledAfterDays);

        // The very same predicates the counts use — see QueueCountsAsync. A chip
        // reading "Stalled (4)" that filters to a different 4 is worse than no
        // chip, so there is exactly one definition of each.
        query = selectedQueue switch
        {
            "needsresponse" => query.Where(NeedsResponsePredicate),
            "blocked"       => query.Where(BlockedPredicate()),
            "stalled"       => query.Where(StalledPredicate(stalledBefore)),
            _               => query,
        };
        var inQueue = selectedQueue is "needsresponse" or "blocked" or "stalled";

        var total = await query.CountAsync();
        var ordered = inQueue
            ? query.OrderBy(d => d.CreatedAt)            // longest-waiting first
            : query.OrderByDescending(d => d.CreatedAt); // newest first (default)
        // Two steps on purpose. The row shape is built by AdminMappers.MapAdminLead
        // in C#, because one of its fields (photoCount) is a JSON parse the
        // database cannot perform — see that mapper. So the query fetches the
        // entity plus the one thing only it can resolve (the routed partner's
        // name, a correlated subquery), and the DTO is shaped afterwards.
        // AsNoTracking because materialising whole entities would otherwise leave
        // a page of leads in the change tracker for a read-only GET.
        var rows = await ordered
            .AsNoTracking()
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(d => new
            {
                Lead = d,
                SupplierName = d.SupplierId == null
                    ? null
                    : Db.Suppliers.Where(s => s.Id == d.SupplierId).Select(s => s.Name).FirstOrDefault(),
            })
            .ToListAsync();

        var leads = rows.Select(r => AdminMappers.MapAdminLead(r.Lead, r.SupplierName)).ToList();

        return Ok(new
        {
            total,
            page,
            limit,
            items    = leads,
            outreach = await OutreachSummaryAsync(rows.Select(r => r.Lead).ToList()),
            queues   = await QueueCountsAsync(filtered),
        });
    }

    /// <summary>
    /// Provider-side state of each lead on the current page, keyed by lead id as
    /// a string (JSON object keys are strings; a Guid key would serialize the
    /// same but leaves the contract to chance).
    ///
    /// Two queries for the whole page, not per row: the operator waits on this
    /// list, and the point of the summary is to remove clicks, not to trade them
    /// for latency.
    ///
    /// The four counts partition the asks — answered + failed + silent == asked —
    /// so the UI never subtracts and can never render a negative. Delivery is
    /// reported separately and NEVER folded into "silent": a row with no receipt
    /// is UNKNOWN, not undelivered (see ProviderOutreach.DeliveredAt), and
    /// collapsing the two would turn our own missing telemetry into an accusation
    /// against the provider.
    ///
    /// <c>stalled</c> is the per-row form of the queue chip's definition, decided
    /// HERE rather than in the browser: a badge that computes its own version of
    /// "stalled" is a second definition, and the day the two disagree the count
    /// and the rows it labels stop describing the same thing. The in-memory rule
    /// below and <see cref="StalledPredicate"/> are held together by a test that
    /// asserts the flagged rows and the count agree.
    /// </summary>
    private async Task<Dictionary<string, object>> OutreachSummaryAsync(List<DemandLead> pageLeads)
    {
        if (pageLeads.Count == 0) return [];
        var leadIds = pageLeads.Select(l => l.Id).ToList();

        var outreach = await Db.ProviderOutreaches
            .AsNoTracking()
            .Where(o => leadIds.Contains(o.DemandLeadId))
            .Select(o => new { o.DemandLeadId, o.SentAt, o.Status, o.DeliveredAt })
            .ToListAsync();

        // Blocked is counted from the ASKS, not from the NeedsInfo status: the
        // status can be moved by hand, the open ProviderInfoRequest is the thing
        // that actually has something to resolve, and it is what the workspace
        // will offer a button for.
        var openAsks = await Db.ProviderInfoRequests
            .AsNoTracking()
            .Where(r => leadIds.Contains(r.DemandLeadId) && r.ResolvedAt == null)
            .Select(r => r.DemandLeadId)
            .ToListAsync();

        var byLead     = outreach.GroupBy(o => o.DemandLeadId).ToDictionary(g => g.Key, g => g.ToList());
        var asksByLead = openAsks.GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
        var stalledBefore = DateTime.UtcNow.AddDays(-StalledAfterDays);

        var summary = new Dictionary<string, object>(pageLeads.Count);
        foreach (var lead in pageLeads)
        {
            var rows     = byLead.GetValueOrDefault(lead.Id) ?? [];
            var answered = rows.Count(o => IsProviderAnswer(o.Status));
            var failed   = rows.Count(o => IsDeliveryFailure(o.Status));

            summary[lead.Id.ToString()] = new
            {
                asked      = rows.Count,
                answered,
                failed,
                silent     = rows.Count - answered - failed,
                delivered  = rows.Count(o => o.DeliveredAt != null),
                // No receipt AND no failure verdict: we simply do not know. Kept
                // as its own number so the UI can say "unknown" in as many words.
                deliveryUnknown = rows.Count(o => o.DeliveredAt == null && !IsDeliveryFailure(o.Status)),
                blocked    = asksByLead.GetValueOrDefault(lead.Id),
                // Null when nobody was ever asked — which is a different thing
                // from "asked a long time ago" and must not render as an age.
                lastSentAt = rows.Count == 0 ? (DateTime?)null : rows.Max(o => o.SentAt),
                // Same rule as StalledPredicate, term for term.
                stalled = (lead.Status == DemandLeadStatus.New
                            || lead.Status == DemandLeadStatus.Contacted
                            || lead.Status == DemandLeadStatus.Quoted)
                        && lead.CreatedAt <= stalledBefore
                        && answered == 0
                        && !rows.Any(o => o.SentAt > stalledBefore),
            };
        }

        return summary;
    }

    /// <summary>Untouched: still New and never contacted (the SLA view).</summary>
    private static readonly System.Linq.Expressions.Expression<Func<DemandLead, bool>>
        NeedsResponsePredicate = d => d.Status == DemandLeadStatus.New && d.ContactedAt == null;

    /// <summary>
    /// A provider is waiting on US. Keyed on the open <see cref="ProviderInfoRequest"/>
    /// rather than on the NeedsInfo status: the status can be retyped by hand, the
    /// ask is the thing that actually has something to resolve.
    /// </summary>
    private System.Linq.Expressions.Expression<Func<DemandLead, bool>> BlockedPredicate() =>
        d => Db.ProviderInfoRequests.Any(r => r.DemandLeadId == d.Id && r.ResolvedAt == null);

    /// <summary>
    /// Stalled: an OPEN request that no provider has answered and where nothing
    /// has been sent for <see cref="StalledAfterDays"/> days — including one
    /// nobody was ever asked about.
    ///
    /// SILENCE, NOT AGE, and the difference is the whole point. A request sent
    /// yesterday to fifteen providers is not stalled; one raised a week ago that
    /// nobody was ever asked about is. Sorting by date would have surfaced the
    /// first and buried the second, which is how five Viljandi requests sat with
    /// no reply of any kind until somebody read the metrics by hand.
    /// </summary>
    private System.Linq.Expressions.Expression<Func<DemandLead, bool>> StalledPredicate(DateTime before) =>
        d => (d.Status == DemandLeadStatus.New
                || d.Status == DemandLeadStatus.Contacted
                || d.Status == DemandLeadStatus.Quoted)
            && d.CreatedAt <= before
            && !Db.ProviderOutreaches.Any(o => o.DemandLeadId == d.Id
                && (o.Status == ProviderOutreachStatus.Replied
                 || o.Status == ProviderOutreachStatus.Declined
                 || o.Status == ProviderOutreachStatus.NeedsInfo))
            && !Db.ProviderOutreaches.Any(o => o.DemandLeadId == d.Id && o.SentAt > before);

    /// <summary>
    /// Counts for the three queues an operator actually works from, over the
    /// whole filtered result set rather than the current page — a chip may only
    /// carry a number if the number is true of everything behind it.
    /// </summary>
    private async Task<object> QueueCountsAsync(IQueryable<DemandLead> filtered)
    {
        var stalledBefore = DateTime.UtcNow.AddDays(-StalledAfterDays);

        return new
        {
            needsResponse = await filtered.CountAsync(NeedsResponsePredicate),
            blocked       = await filtered.CountAsync(BlockedPredicate()),
            stalled       = await filtered.CountAsync(StalledPredicate(stalledBefore)),
            // Echoed so the row badges and this count can never disagree about
            // what "stalled" means.
            stalledAfterDays = StalledAfterDays,
        };
    }

    [HttpPatch("leads/{id:guid}")]
    public async Task<IActionResult> UpdateLead(Guid id, [FromBody] UpdateLeadRequest body)
    {
        var lead = await Db.DemandLeads.FindAsync(id);
        if (lead is null) return NotFound(Error("Lead not found."));

        if (!string.IsNullOrWhiteSpace(body.Status)
            && Enum.TryParse<DemandLeadStatus>(body.Status, ignoreCase: true, out var requestedStatus)
            && Enum.IsDefined(requestedStatus)
            && requestedStatus == DemandLeadStatus.Converted)
        {
            return Conflict(Error("Confirm the customer's chosen offer instead."));
        }

        // ── Request-field corrections (partial: null/omitted = unchanged) ─────
        // The admin can fix what the customer submitted. Everything is validated
        // BEFORE any mutation so a single bad field can't leave a half-applied
        // edit. For optional fields an explicit empty/whitespace value clears the
        // field (mirrors the Clamp semantics in the public concierge intake); a
        // JSON-null / omitted field is left untouched.
        static bool HasAngle(string s) => s.Contains('<') || s.Contains('>');
        static string? ClampOpt(string s, int max)
        {
            var t = s.Trim();
            return t.Length == 0 ? null : (t.Length > max ? t[..max] : t);
        }

        // Validate the fields that can reject up front.
        if (body.Email is not null && !EmailValidation.IsValid(body.Email))
            return BadRequest(Error("Invalid email."));

        DemandLeadCategory? newCategory = null;
        if (body.Category is not null)
        {
            var slug = body.Category.Trim().ToLowerInvariant();
            if (slug == "any")
                newCategory = DemandLeadCategory.Any;
            else if (ServiceCategories.BySlug.TryGetValue(slug, out var cat))
                newCategory = cat;
            else
                return BadRequest(Error("Unknown category."));
        }

        // A provided-but-unparseable status is an admin typo (e.g. "lost"), not a
        // no-op: reject it up front so a silent 200-with-no-change can't masquerade
        // as a successful transition. Omitted/blank status = leave unchanged.
        DemandLeadStatus? newStatus = null;
        if (!string.IsNullOrWhiteSpace(body.Status))
        {
            if (!Enum.TryParse<DemandLeadStatus>(body.Status, ignoreCase: true, out var parsedStatus)
                || !Enum.IsDefined(parsedStatus))
                return BadRequest(Error($"Invalid status '{body.Status}'."));
            newStatus = parsedStatus;
        }

        string? newCity = null;
        if (body.City is not null)
        {
            if (HasAngle(body.City)) return BadRequest(Error("City contains invalid characters."));
            var c = body.City.Trim();
            if (c.Length == 0) return BadRequest(Error("City cannot be empty."));
            newCity = c.Length > 100 ? c[..100] : c;
        }

        if (body.Name    is not null && HasAngle(body.Name))    return BadRequest(Error("Name contains invalid characters."));
        if (body.ToCity  is not null && HasAngle(body.ToCity))  return BadRequest(Error("Destination contains invalid characters."));
        if (body.Details is not null && HasAngle(body.Details)) return BadRequest(Error("Details contain invalid characters."));

        // needDate is a string like every other editable field ("" clears, a valid
        // date sets, a malformed date is a real 400): DateTime? can't JSON-bind ""
        // (the value the frontend sends on clear) — it 400s the whole edit before
        // this handler runs. Only parse when provided AND non-empty; provided-empty
        // is a deliberate clear that leaves newNeedDate null.
        DateTime? newNeedDate = null;
        if (!string.IsNullOrWhiteSpace(body.NeedDate))
        {
            // RoundtripKind keeps a trailing 'Z' as UTC and a bare date as Unspecified
            // (no timezone shift either way); .Date + SpecifyKind(Utc) then gives the
            // calendar date at UTC midnight — Npgsql rejects Unspecified on timestamptz.
            if (!DateTime.TryParse(body.NeedDate, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var parsedDate))
                return BadRequest(Error("Invalid date."));
            newNeedDate = DateTime.SpecifyKind(parsedDate.Date, DateTimeKind.Utc);
        }

        // All request-field validation passed — apply the corrections.
        var requestEdited = false;
        if (body.Email is not null) { lead.Email = body.Email.Trim(); requestEdited = true; }
        if (newCity is not null)    { lead.City = newCity; requestEdited = true; }
        if (newCategory is { } nc)  { lead.Category = nc; requestEdited = true; }
        if (body.Name is not null)    { lead.Name    = ClampOpt(body.Name, 120);   requestEdited = true; }
        if (body.Phone is not null)   { lead.Phone   = ClampOpt(body.Phone, 40);   requestEdited = true; }
        if (body.ToCity is not null)  { lead.ToCity  = ClampOpt(body.ToCity, 100); requestEdited = true; }
        if (body.Details is not null) { lead.Details = ClampOpt(body.Details, 2000); requestEdited = true; }
        if (body.NeedDate is not null)
        {
            // Provided: "" → clear (newNeedDate stayed null), a valid date → set.
            lead.NeedDate = newNeedDate;
            requestEdited = true;
        }

        if (newStatus is { } ns)
        {
            // Shared transition logic — stamps the first-touch ContactedAt once
            // (also used by the offer loop's auto-transitions).
            DemandLeadLifecycle.MoveTo(lead, ns);
        }

        if (body.AdminNotes is not null)
            lead.AdminNotes = body.AdminNotes;

        Audit("demand_lead.updated", User.GetUserId().ToString(), lead.Id.ToString(),
              $"Status: {lead.Status}, Notes: {(body.AdminNotes is not null ? "updated" : "unchanged")}, " +
              $"Fields: {(requestEdited ? "edited" : "unchanged")}");

        await Db.SaveChangesAsync();

        return Ok(new
        {
            lead.Id,
            lead.Name,
            lead.Email,
            lead.Phone,
            lead.City,
            category   = lead.Category.ToString().ToLower(),
            lead.ToCity,
            lead.NeedDate,
            lead.Details,
            status     = lead.Status.ToString().ToLower(),
            lead.AdminNotes,
        });
    }

    /// <summary>
    /// HARD-deletes a demand lead: the row is gone, permanently, along with
    /// everything that references it (provider outreach rows, offers and their
    /// options), all inside one transaction.
    ///
    /// This exists for ONE reason: removing test/canary/spam rows so the ops
    /// metrics stay honest. Dismissed leads still count in
    /// <see cref="GetLeadMetrics"/> — requestsThisWeek, requests30d and every
    /// rate denominator — so a handful of smoke-test rows silently inflates the
    /// north-star numbers the founder reports.
    ///
    /// It is NOT the way to close a real request that went nowhere. For that,
    /// PATCH the status to Dismissed/Unmatched: a real lost lead is data — it
    /// belongs in the denominator, and erasing it would flatter the conversion
    /// rates instead of measuring them.
    /// </summary>
    /// <response code="204">Lead and its dependent rows were deleted.</response>
    /// <response code="404">No lead with that id.</response>
    [HttpDelete("leads/{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteLead(Guid id)
    {
        var lead = await Db.DemandLeads.FindAsync(id);
        if (lead is null) return NotFound(Error("Lead not found."));

        // Snapshot the identifying facts BEFORE the row disappears — the audit
        // trail is the only thing that outlives a hard delete.
        var city      = lead.City;
        var category  = lead.Category.ToString().ToLowerInvariant();
        var source    = lead.Source ?? "none";
        var status    = lead.Status;
        var createdAt = lead.CreatedAt;
        var name      = string.IsNullOrWhiteSpace(lead.Name) ? "(no name)" : lead.Name;

        await using var tx = await Db.Database.BeginTransactionAsync();
        try
        {
            // Leaf → root. Offer and ProviderOutreach both FK the lead (and
            // OfferOption FKs the offer); the mapping cascades, but deleting the
            // dependents explicitly keeps the behaviour identical on Npgsql and
            // the InMemory provider instead of leaning on DB-side cascade rules.
            var offers = await Db.Offers.Where(o => o.DemandLeadId == id).ToListAsync();
            if (offers.Count > 0)
            {
                var offerIds = offers.Select(o => o.Id).ToList();
                var options  = await Db.OfferOptions
                    .Where(o => offerIds.Contains(o.OfferId)).ToListAsync();
                Db.OfferOptions.RemoveRange(options);
                Db.Offers.RemoveRange(offers);
            }

            // Before the outreach rows they hang off: a provider's "I can't quote
            // this yet" is a fact about one specific outreach and means nothing
            // without it. Deleted explicitly for the same reason as everything
            // else here — so a canary lead leaves nothing behind on the InMemory
            // provider either, rather than only where the DB enforces cascades.
            var infoRequests = await Db.ProviderInfoRequests
                .Where(r => r.DemandLeadId == id).ToListAsync();
            Db.ProviderInfoRequests.RemoveRange(infoRequests);

            var outreaches = await Db.ProviderOutreaches
                .Where(o => o.DemandLeadId == id).ToListAsync();
            Db.ProviderOutreaches.RemoveRange(outreaches);

            Db.DemandLeads.Remove(lead);

            Audit("lead.deleted", User.GetUserId().ToString(), id.ToString(),
                  $"Hard-deleted lead \"{name}\" — city: {city}, category: {category}, " +
                  $"source: {source}, status: {status}, created: {createdAt:yyyy-MM-dd}. " +
                  $"Removed {outreaches.Count} outreach row(s) and {offers.Count} offer(s).");

            await Db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        return NoContent();
    }

    /// <summary>
    /// Closes a provider's "I cannot price this yet" (<see cref="ProviderInfoRequest"/>):
    /// ops has answered the question, so the provider can quote.
    ///
    /// WHY THIS EXISTS AT ALL. The ask could be raised but never lowered — nothing
    /// in the system could write <c>ResolvedAt</c>. So the quote page went on
    /// telling the provider we still owed them an answer after we had sent it, and
    /// the outreach row sat on NeedsInfo forever. A flag with no off switch stops
    /// meaning "blocked" within a week and starts meaning "was blocked once".
    ///
    /// WHY THE OUTREACH GOES BACK TO <c>Sent</c>, and to nothing else:
    ///
    ///   • NOT <c>Replied</c>, which is the tempting one. GetLeadMetrics counts a
    ///     Replied outreach as a supplier MATCH — the concierge north-star the
    ///     founder reports. Answering somebody's question is not that provider
    ///     agreeing to serve the job; it is us unblocking them so they can decide.
    ///     Landing on Replied here would inflate match rate with exactly the
    ///     requests we are most stuck on, silently, and it is the whole reason
    ///     NeedsInfo was appended to the enum instead of reusing Replied.
    ///   • NOT <c>NoAnswer</c>, even though the flip could have come from there.
    ///     NoAnswer is an admin's guess that the provider never came back, and the
    ///     question they sent is the proof it was wrong; restoring it would
    ///     re-assert a fact we know to be false.
    ///   • <c>Sent</c> says exactly what is now true: they were contacted, the ball
    ///     is in their court, and no price has arrived. That is where an outreach
    ///     waits, so the row rejoins the normal queue instead of needing its own.
    ///
    /// Only when the row is STILL on NeedsInfo. A provider who submitted a price
    /// while the question was open is already Replied, and a dead address is
    /// Bounced/Complained — those are stronger facts than "we answered them", and
    /// resetting one to Sent would erase a real quote from the ops view. Same
    /// guard, same reasoning, as the flip in QuoteController.NeedInfo.
    ///
    /// IDEMPOTENT. Resolving an already-resolved ask is a 200 that changes
    /// nothing: it does not re-stamp ResolvedAt (which would rewrite when we
    /// actually answered), does not touch the outreach (which may legitimately
    /// have moved on since), and writes no second audit row. Two operators
    /// clicking the same button, or one double-click, must not be an error the
    /// person has to interpret mid-loop.
    /// </summary>
    /// <response code="200">Resolved — or already was.</response>
    /// <response code="404">No info request with that id.</response>
    [HttpPost("info-requests/{id:guid}/resolve")]
    public async Task<IActionResult> ResolveInfoRequest(Guid id)
    {
        var request = await Db.ProviderInfoRequests.FindAsync(id);
        if (request is null) return NotFound(Error("Info request not found."));

        var outreach = await Db.ProviderOutreaches.FindAsync(request.ProviderOutreachId);

        if (request.ResolvedAt is null)
        {
            request.ResolvedAt = DateTime.UtcNow;

            if (outreach is { Status: ProviderOutreachStatus.NeedsInfo })
                outreach.Status = ProviderOutreachStatus.Sent;

            // Names the reasons, because the row itself stops being able to: once
            // ResolvedAt is set the workspace no longer shows it, and the audit
            // trail becomes the only record of what the provider was blocked on.
            var reasons = InfoRequestReasons.Parse(request.ReasonsJson);
            Audit("lead.info_request_resolved", User.GetUserId().ToString(), request.Id.ToString(),
                  $"Lead {request.DemandLeadId}, supplier {request.SupplierId}, " +
                  $"outreach {request.ProviderOutreachId}. Asked for: " +
                  $"{(reasons.Count == 0 ? "not itemised" : string.Join(", ", reasons))}. " +
                  $"Outreach now: {(outreach is null ? "missing" : outreach.Status.ToString())}.");

            await Db.SaveChangesAsync();
        }

        return Ok(new
        {
            id                 = request.Id,
            providerOutreachId = request.ProviderOutreachId,
            resolvedAt         = request.ResolvedAt,
            outreachStatus     = outreach?.Status.ToString().ToLower(),
        });
    }

    /// <summary>
    /// Streams one of the customer's photos to the admin.
    ///
    /// The founder runs the whole match loop by hand out of this workspace, and
    /// until now he was the only participant who could NOT see the pictures: the
    /// provider gets them through their quote token, the customer sent them, and
    /// ops was left unable to say whether they were good enough to quote from,
    /// answer a provider's question about them, or tell a customer to re-shoot.
    ///
    /// Addressed by INDEX, not by storage key — the same contract as the
    /// provider's <c>GET /api/quote/{token}/photos/{index}</c>. The index is
    /// resolved against THIS lead's own list, so the id in the URL is the only
    /// thing that decides which private objects are reachable; the keys never
    /// leave the server, and no caller can name an object of their own.
    ///
    /// Admin-only by inheritance from <see cref="AdminBaseController"/> — there
    /// is no token or public form of this route.
    /// </summary>
    /// <response code="200">The photo, as JPEG.</response>
    /// <response code="404">Unknown lead, or no photo at that index.</response>
    [HttpGet("leads/{id:guid}/photos/{index:int}")]
    public async Task<IActionResult> GetLeadPhoto(
        Guid id, int index, [FromServices] IStorageService storage, CancellationToken ct)
    {
        // Bodiless 404s, unlike the JSON Error() the rest of this controller
        // returns: the caller is an image fetch, not a JSON consumer, and a
        // missing photo is rendered as a broken tile either way.
        if (index < 0) return NotFound();

        var lead = await Db.DemandLeads
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (lead is null) return NotFound();

        // Malformed JSON in the column yields an empty list rather than throwing
        // (see LeadPhotos), so a bad row is a 404 here, not a 500.
        var keys = LeadPhotos.Keys(lead.PhotoKeysJson);
        if (index >= keys.Count) return NotFound();

        var bytes = await storage.DownloadPrivateAsync(keys[index]);
        if (bytes is null) return NotFound();

        // Everything stored by this feature is re-encoded JPEG — see
        // LeadPhotoNormalizer — so the type is known rather than sniffed.
        // no-store: a customer's home should not linger in a shared cache.
        Response.Headers.CacheControl = "private, no-store";
        return File(bytes, "image/jpeg");
    }

    [HttpGet("leads/{id:guid}/provider-candidates")]
    public async Task<IActionResult> GetProviderCandidates(
        Guid id,
        [FromQuery] string? q = null,
        [FromQuery] string scope = "nearby",
        [FromQuery] string category = "lead",
        [FromQuery] double radiusKm = 25,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var lead = await Db.DemandLeads.FindAsync([id], ct);
        if (lead is null) return NotFound(Error("Lead not found."));

        var allEstonia = string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase);
        if (!allEstonia && !string.Equals(scope, "nearby", StringComparison.OrdinalIgnoreCase))
            return BadRequest(Error("scope must be nearby or all."));

        var allCategories = string.Equals(category, "any", StringComparison.OrdinalIgnoreCase);
        if (!allCategories && !string.Equals(category, "lead", StringComparison.OrdinalIgnoreCase))
            return BadRequest(Error("category must be lead or any."));
        if (allCategories && !allEstonia)
            return BadRequest(Error("category=any requires scope=all."));

        var search = new ProviderCandidateSearch(
            q?.Trim(), allEstonia, allCategories,
            Math.Clamp(radiusKm, 1, 250), Math.Clamp(limit, 1, 200));
        return Ok(await ProviderCandidateFinder.SearchAsync(Db, lead, search, ct));
    }

    /// <summary>
    /// Top match suggestions for a concierge lead: active listings from active
    /// suppliers in the lead's category (or all categories for Any), same-city
    /// listings first, then most recently updated — UNIONed with active directory
    /// suppliers (unclaimed profiles) whose service types cover the lead's
    /// category, again same-city first. Listing-based rows come first; directory
    /// rows carry null listing/price fields. Capped at 10 total. Powers the admin
    /// "who can serve this request" view.
    /// </summary>
    [HttpGet("leads/{id:guid}/matches")]
    public async Task<IActionResult> GetLeadMatches(Guid id)
    {
        var lead = await Db.DemandLeads.FindAsync(id);
        if (lead is null) return NotFound(Error("Lead not found."));

        var query = Db.Listings
            .Where(l => l.IsActive && l.Supplier != null && l.Supplier.IsActive);

        if (lead.Category != DemandLeadCategory.Any)
        {
            // Directory-only categories (cleaning, packing, vanrental, insurance)
            // have no ListingType — for those, only directory suppliers can match.
            ListingType? type = lead.Category switch
            {
                DemandLeadCategory.Warehouse => ListingType.Warehouse,
                DemandLeadCategory.Moving    => ListingType.Moving,
                DemandLeadCategory.Trailer   => ListingType.Trailer,
                _                            => null,
            };
            query = type is { } t
                ? query.Where(l => l.Type == t)
                : query.Where(l => false);
        }

        // ToLower (not ILike) so the same expression runs on both Npgsql and the
        // InMemory test provider.
        var leadCity = (lead.City ?? "").ToLower();
        var matches = await query
            .OrderByDescending(l => leadCity != "" && l.City != null && l.City.ToLower() == leadCity)
            .ThenByDescending(l => l.UpdatedAt)
            .Take(10)
            .Select(l => new
            {
                supplierId   = l.SupplierId,
                supplierName = l.Supplier.Name,
                contactEmail = l.Supplier.ContactEmail,
                contactPhone = l.Supplier.ContactPhone,
                listingId    = (Guid?)l.Id,
                listingTitle = (string?)l.Title,
                listingCity  = (string?)l.City,
                price        = (decimal?)l.PriceFrom,
                priceUnit    = (string?)l.PriceUnit,
                isDirectory  = false,
                serviceTypes = new List<string>(),
            })
            .ToListAsync();

        var remaining = 10 - matches.Count;
        if (remaining > 0)
        {
            var directoryQuery = Db.Suppliers
                .Where(s => s.IsDirectoryListing && s.IsActive);

            if (lead.Category != DemandLeadCategory.Any)
            {
                // ServiceTypesJson holds a JSON array of plain lowercase slugs, so
                // quoted-token containment is exact and runs on both Npgsql (LIKE)
                // and the InMemory provider.
                var slugToken = $"\"{ServiceCategories.SlugFor(lead.Category)}\"";
                directoryQuery = directoryQuery.Where(s =>
                    s.ServiceTypesJson != null && s.ServiceTypesJson.Contains(slugToken));
            }

            var directorySuppliers = await directoryQuery
                .Select(s => new
                {
                    s.Id,
                    s.Name,
                    s.ContactEmail,
                    s.ContactPhone,
                    s.UpdatedAt,
                    s.ServiceTypesJson,
                    // Best-matching active location city: lead-city match first.
                    City = Db.SupplierLocations
                        .Where(l => l.SupplierId == s.Id && l.IsActive)
                        .OrderByDescending(l => leadCity != "" && l.City.ToLower() == leadCity)
                        .Select(l => (string?)l.City)
                        .FirstOrDefault(),
                })
                .ToListAsync();

            var listingSupplierIds = matches.Select(m => m.supplierId).ToHashSet();
            var directoryRows = directorySuppliers
                .Where(d => !listingSupplierIds.Contains(d.Id))
                .OrderByDescending(d => leadCity != "" && (d.City ?? "").ToLower() == leadCity)
                .ThenByDescending(d => d.UpdatedAt)
                .Take(remaining)
                .Select(d => new
                {
                    supplierId   = d.Id,
                    supplierName = d.Name,
                    contactEmail = d.ContactEmail,
                    contactPhone = d.ContactPhone,
                    listingId    = (Guid?)null,
                    listingTitle = (string?)null,
                    listingCity  = d.City,
                    price        = (decimal?)null,
                    priceUnit    = (string?)null,
                    isDirectory  = true,
                    // Which services this directory supplier covers — essential for
                    // routing Any-category leads where no slug filter was applied.
                    serviceTypes = ServiceCategories.ParseServiceTypes(d.ServiceTypesJson),
                });

            matches = matches.Concat(directoryRows).ToList();
        }

        return Ok(matches);
    }

    /// <summary>
    /// The providers who have NEVER answered anything — the roll-call behind the
    /// quote rate.
    ///
    /// WHY IT IS ITS OWN ENDPOINT. Every other number here describes requests;
    /// this one describes companies, and it is the only view in which the actual
    /// problem is legible. Five Viljandi storage requests reached 18 providers —
    /// three of them based in Viljandi — and produced no reply of any kind. Per
    /// request that reads as five unlucky weeks. Per company it reads as a supply
    /// side that does not answer email, which is a different business problem
    /// with a different fix (phone, a different ask, or a different city).
    ///
    /// ALL-TIME AND ALL CHANNELS, deliberately, unlike the concierge-scoped
    /// funnel above: "has this company ever once replied to us" is a fact about
    /// the company, and a reply to a routed request is still a reply. Narrowing
    /// it to 30 days would also let a provider who answered once last spring
    /// reappear here as if they had never spoken to us.
    ///
    /// DELIVERY IS REPORTED, NEVER INFERRED. A silent provider whose mail we
    /// know arrived is a rejection of the ask; one whose mail bounced is a dead
    /// address; one with no receipt at all is UNKNOWN — most of the history,
    /// since the webhook only started stamping on 2026-08-18. Three different
    /// actions, so they are three different columns and none of them is folded
    /// into the others.
    ///
    /// Aggregated in memory for the same reason as the funnel above — outreach
    /// volume is in the hundreds, and the arithmetic stays identical on Npgsql
    /// and the InMemory provider.
    /// </summary>
    /// <param name="limit">How many silent providers to return (1-100, worst first).</param>
    [HttpGet("leads/provider-silence")]
    public async Task<IActionResult> GetProviderSilence([FromQuery] int limit = 20)
    {
        limit = Math.Clamp(limit, 1, 100);

        var rows = await Db.ProviderOutreaches
            .AsNoTracking()
            .Select(o => new { o.SupplierId, o.SentAt, o.Status, o.DeliveredAt, o.QuotedAt })
            .ToListAsync();

        var bySupplier = rows.GroupBy(o => o.SupplierId).ToList();

        // A submitted price counts as an answer even if nobody moved the status:
        // QuotedAt is the provider typing a number into our own quote page, and
        // listing them as never-heard-from would be flatly false.
        var silent = bySupplier
            .Where(g => g.All(o => !IsProviderAnswer(o.Status) && o.QuotedAt == null))
            .Select(g => new
            {
                SupplierId      = g.Key,
                Asked           = g.Count(),
                LastAskedAt     = g.Max(o => o.SentAt),
                Delivered       = g.Count(o => o.DeliveredAt != null),
                Failed          = g.Count(o => IsDeliveryFailure(o.Status)),
                DeliveryUnknown = g.Count(o => o.DeliveredAt == null && !IsDeliveryFailure(o.Status)),
            })
            // Worst first: the most asks wasted, then the coldest — that is the
            // order in which the founder would drop them or pick up the phone.
            .OrderByDescending(s => s.Asked)
            .ThenBy(s => s.LastAskedAt)
            .ToList();

        var top   = silent.Take(limit).ToList();
        var ids   = top.Select(s => s.SupplierId).ToList();
        var names = await Db.Suppliers
            .AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.ContactEmail,
                s.ContactEmailUnusable,
                // Supplier itself has no city — it hangs off the locations. Worth
                // the subquery: "three of them are IN Viljandi" is the detail that
                // turns a list of names into a decision about a town.
                City = Db.SupplierLocations
                    .Where(l => l.SupplierId == s.Id && l.IsActive)
                    .Select(l => (string?)l.City)
                    .FirstOrDefault(),
            })
            .ToDictionaryAsync(s => s.Id);

        return Ok(new
        {
            // The headline: of everyone we have ever emailed, how many have never
            // said a word. Both numbers, never just the rate — at this volume a
            // percentage on its own would be noise dressed as a measurement.
            providersContacted = bySupplier.Count,
            providersSilent    = silent.Count,
            asksUnanswered     = silent.Sum(s => s.Asked),
            items = top.Select(s =>
            {
                var supplier = names.GetValueOrDefault(s.SupplierId);
                return new
                {
                    supplierId   = s.SupplierId,
                    supplierName = supplier?.Name,
                    city         = supplier?.City,
                    // Surfaced because it changes the verdict: a provider with a
                    // retired address never had the chance to answer.
                    contactEmail         = supplier?.ContactEmail,
                    contactEmailUnusable = supplier?.ContactEmailUnusable ?? false,
                    asked                = s.Asked,
                    lastAskedAt          = s.LastAskedAt,
                    delivered            = s.Delivered,
                    deliveryFailed       = s.Failed,
                    deliveryUnknown      = s.DeliveryUnknown,
                };
            }).ToList(),
        });
    }

    /// <summary>
    /// Concierge north-star funnel — the FUNDING STORY, computed honestly over
    /// the demand funnel ONLY (Source == "concierge"). Partner-direct, routed-
    /// quote and notify-interest leads are a different channel and must never
    /// pollute requests-per-week or any funnel rate. Rates are computed in memory
    /// over the 30-day slice — lead volumes are tiny and this keeps the math
    /// identical on Npgsql and the InMemory provider.
    ///
    /// The four named north-stars: requestsThisWeek (qualified requests/week),
    /// matchRate30d (supplier match rate), quoteRate30d/bookingRate30d
    /// (quote→booking conversion) and medianFirstResponseMinutes (time-to-first-
    /// response). contactRate30d/medianFirstResponseMinutes count only GENUINE
    /// contact (Contacted/Quoted/Converted with a real ContactedAt) — dismissed
    /// and unmatched leads are closures, not contact, so they can't make the ops
    /// team look artificially fast or inflate the contact rate.
    ///
    /// outreachDelivery30d is the provider-side half of the same story — did the
    /// ask even arrive — and is measured over a deliberately narrower base than
    /// everything else here; the comment on it explains why.
    /// </summary>
    [HttpGet("leads/metrics")]
    public async Task<IActionResult> GetLeadMetrics()
    {
        const string ConciergeSource = "concierge";
        var now      = DateTime.UtcNow;
        var weekAgo  = now.AddDays(-7);
        var monthAgo = now.AddDays(-30);

        var requestsThisWeek = await Db.DemandLeads
            .CountAsync(d => d.Source == ConciergeSource && d.CreatedAt >= weekAgo);

        var last30 = await Db.DemandLeads
            .Where(d => d.Source == ConciergeSource && d.CreatedAt >= monthAgo)
            .Select(d => new { d.Id, d.CreatedAt, d.Status, d.ContactedAt, d.RespondedAt, d.QuotedPrice })
            .ToListAsync();

        var requests30d    = last30.Count;
        // Genuine contact == a real ContactedAt, full stop. MoveTo stamps
        // ContactedAt ONLY on genuine-contact transitions (Contacted/Quoted/
        // Converted) and never clears it, so a non-null ContactedAt already means
        // "was genuinely contacted at some point" — INCLUDING a lead later closed
        // to Dismissed(Lost)/Unmatched, which is the normal end state of a
        // contacted-but-didn't-book request (Received→Contacted→Quoted→Lost). Also
        // gating on the CURRENT status would wrongly drop those closed leads while
        // quotedOrBeyond survives closure (QuotedPrice != null), so contactRate30d
        // could fall BELOW quoteRate30d — a logically impossible funnel inversion.
        // Spam New→Dismissed never got a ContactedAt, so it stays excluded here.
        var contactedLeads = last30
            .Where(d => d.ContactedAt != null)
            .ToList();
        var contacted      = contactedLeads.Count;
        var quotedOrBeyond = last30.Count(d => d.QuotedPrice != null
                                            || d.Status == DemandLeadStatus.Quoted
                                            || d.Status == DemandLeadStatus.Converted);
        var converted      = last30.Count(d => d.Status == DemandLeadStatus.Converted);

        // ── Supplier match rate (a NAMED north-star) ─────────────────────────
        // matched  = a concierge request the ops team could actually serve: it
        //            reached Quoted/Converted, OR carries a live offer
        //            (Sent/Viewed/Chosen), OR a provider replied to outreach.
        // total    = concierge requests in the window that have LEFT New (i.e.
        //            the ops team started working them) — an untouched New lead
        //            is not yet a match or a miss, so it's excluded from the base.
        // Unmatched is the explicit miss (worked, but no partner could serve it).
        var leftNew = last30.Where(d => d.Status != DemandLeadStatus.New).ToList();
        var matchBase = leftNew.Count;
        int matched = 0;
        if (matchBase > 0)
        {
            var leftNewIds = leftNew.Select(d => d.Id).ToList();
            var offerMatchedIds = await Db.Offers
                .Where(o => leftNewIds.Contains(o.DemandLeadId)
                         && (o.Status == OfferStatus.Sent
                          || o.Status == OfferStatus.Viewed
                          || o.Status == OfferStatus.Chosen))
                .Select(o => o.DemandLeadId)
                .Distinct()
                .ToListAsync();
            var repliedOutreachIds = await Db.ProviderOutreaches
                .Where(o => leftNewIds.Contains(o.DemandLeadId)
                         && o.Status == ProviderOutreachStatus.Replied)
                .Select(o => o.DemandLeadId)
                .Distinct()
                .ToListAsync();
            var matchSignalIds = offerMatchedIds.Concat(repliedOutreachIds).ToHashSet();

            matched = leftNew.Count(d =>
                d.Status == DemandLeadStatus.Quoted
                || d.Status == DemandLeadStatus.Converted
                || matchSignalIds.Contains(d.Id));
        }

        // ── Outreach delivery telemetry (2026-08-18) ─────────────────────────
        // Decomposes the provider side into sent → delivered → opened, so that
        // "we asked 18 providers and nobody answered" can be told apart from "we
        // asked 18 providers and the mail never arrived". Those need opposite
        // fixes — a better ask versus a deliverability repair — and until the
        // Resend webhook was actually configured (it never had been; see
        // ResendWebhookController) nothing stored could distinguish them.
        //
        // THE DENOMINATOR IS THE WHOLE PROBLEM HERE. DeliveredAt/OpenedAt can
        // only be written by that webhook, and the columns are deliberately not
        // backfilled, so every row sent before it existed is UNKNOWN — not
        // undelivered. A rate over all outreach would therefore report a
        // delivery collapse that is really just our own history, and it would
        // stay wrong forever rather than converging.
        //
        // So the measurable base starts at the first outreach row that ever
        // received a receipt. Derived from the data rather than hard-coded to a
        // date: nobody has to remember to edit a constant, it is correct on any
        // environment (a fresh staging DB has its own start), and before the
        // first receipt ever lands it reports nulls rather than a confident 0%.
        // Rows in the window that predate it are surfaced as unknownSent, so the
        // blind spot is visible instead of silently folded into either number.
        var telemetryFrom = await Db.ProviderOutreaches
            .Where(o => o.DeliveredAt != null || o.OpenedAt != null)
            .OrderBy(o => o.SentAt)
            .Select(o => (DateTime?)o.SentAt)
            .FirstOrDefaultAsync();

        // Concierge-scoped like everything else on this endpoint: outreach for a
        // partner-direct or routed lead is a different channel and must not move
        // the demand funnel. Windowed on the OUTREACH's own SentAt, not the
        // lead's CreatedAt — this measures messages, and an old lead worked last
        // week is exactly the message we want counted.
        var outreach30d = await (
            from o in Db.ProviderOutreaches
            join d in Db.DemandLeads on o.DemandLeadId equals d.Id
            where d.Source == ConciergeSource && o.SentAt >= monthAgo
            select new { o.SentAt, o.DeliveredAt, o.OpenedAt })
            .ToListAsync();

        var measurable = outreach30d
            .Where(o => telemetryFrom != null && o.SentAt >= telemetryFrom.Value)
            .ToList();
        var measuredSent   = measurable.Count;
        var deliveredCount = measurable.Count(o => o.DeliveredAt != null);
        var openedCount    = measurable.Count(o => o.OpenedAt != null);

        // Median minutes from creation to first genuine touch (admin ContactedAt,
        // or an earlier partner RespondedAt). Sample is the genuine-contact set —
        // never dismissed/unmatched closures. Null when nothing was contacted yet.
        var responseMinutes = contactedLeads
            .Select(d =>
            {
                var contactedAt = d.ContactedAt!.Value;
                var first = d.RespondedAt is { } r && r < contactedAt ? r : contactedAt;
                return (first - d.CreatedAt).TotalMinutes;
            })
            .Where(m => m >= 0)
            .OrderBy(m => m)
            .ToList();

        int? medianFirstResponseMinutes = null;
        if (responseMinutes.Count > 0)
        {
            var mid = responseMinutes.Count / 2;
            var median = responseMinutes.Count % 2 == 1
                ? responseMinutes[mid]
                : (responseMinutes[mid - 1] + responseMinutes[mid]) / 2.0;
            medianFirstResponseMinutes = (int)Math.Round(median);
        }

        return Ok(new
        {
            requestsThisWeek,
            requests30d,
            contactRate30d = requests30d == 0 ? 0d : contacted / (double)requests30d,
            quoteRate30d   = requests30d == 0 ? 0d : quotedOrBeyond / (double)requests30d,
            bookingRate30d = converted / (double)Math.Max(1, quotedOrBeyond),
            // Supplier match rate: {matched, total, rate}. total = concierge
            // requests that left New; rate = matched / total (0 when total is 0).
            matchRate30d = new
            {
                matched,
                total = matchBase,
                rate  = matchBase == 0 ? 0d : matched / (double)matchBase,
            },
            // Provider-side delivery funnel over the same 30 days. Rates are
            // null, never 0, when the base is empty: "no measurable outreach
            // yet" and "nothing was delivered" are opposite conclusions, and
            // this metric exists precisely because they were being confused.
            //
            // Both rates share the sent base rather than nesting opened inside
            // delivered — they answer two different questions ("did it arrive",
            // "did anyone look"), and an open whose delivery receipt was missed
            // cannot then produce an open rate above 100%. Note that a null
            // OpenedAt is WEAK evidence (tracking pixels are widely blocked), so
            // openRate is a floor on attention, not a measurement of it; a low
            // deliveredRate is the alarming one.
            outreachDelivery30d = new
            {
                // Null until the very first receipt ever arrives — the marker
                // that everything below is unknown rather than zero.
                measuredFrom  = telemetryFrom,
                sent          = measuredSent,
                delivered     = deliveredCount,
                opened        = openedCount,
                deliveredRate = measuredSent == 0 ? (double?)null : deliveredCount / (double)measuredSent,
                openRate      = measuredSent == 0 ? (double?)null : openedCount / (double)measuredSent,
                // Outreach in the window sent before telemetry existed. Genuinely
                // unknown, so it is reported separately and NEVER counted as
                // undelivered.
                unknownSent   = outreach30d.Count - measuredSent,
            },
            medianFirstResponseMinutes,
        });
    }

    // ─── Operator correspondence ──────────────────────────────────────────────

    /// <summary>
    /// Send a message about this lead — to the customer, or to a provider already
    /// contacted for it — FROM the Ruumly sender, and record that it happened.
    ///
    /// WHY THIS ENDPOINT EXISTS. The concierge loop is a conversation, but the
    /// product could only ever send the letters it composed itself. Every other
    /// message an operator actually needs to send — "what is the exact address?",
    /// "is that price per hour or for the job?", "nobody in your area can take
    /// this" — had no path, so it went from a personal mailbox instead. On
    /// 2026-08-20 four such messages about one live Latvian request, including
    /// one to the customer, left from the founder's personal Gmail signed as
    /// Ruumly; both provider replies then landed where the ops loop cannot see
    /// them. The missing endpoint was the cause, so this is the fix.
    ///
    /// THE RECIPIENT IS NEVER SUPPLIED BY THE CALLER. That is the whole safety
    /// design: this is an authenticated "send arbitrary text to an email address"
    /// endpoint, which is a spam cannon if the address is a parameter. The caller
    /// names a ROLE — the customer, or a supplier id — and the address is
    /// resolved from the lead's own data: the customer's own email, or a supplier
    /// that has a ProviderOutreach row FOR THIS LEAD. A provider we never wrote
    /// to about this request cannot be reached through here at all.
    ///
    /// Reply-To is the ops inbox, not the sender, so an answer lands where the
    /// team reads rather than in one person's mail.
    /// </summary>
    [HttpPost("leads/{id:guid}/messages")]
    public async Task<IActionResult> SendLeadMessage(Guid id, [FromBody] SendLeadMessageRequest body)
    {
        var lead = await Db.DemandLeads.FindAsync(id);
        if (lead is null) return NotFound(Error("Lead not found."));

        if (body is null) return BadRequest(Error("Nothing to send."));

        var subject = (body.Subject ?? "").Trim();
        var text    = (body.Body ?? "").Trim();
        if (subject.Length == 0) return BadRequest(Error("Subject is required."));
        if (text.Length == 0)    return BadRequest(Error("Message body is required."));
        if (subject.Length > 300)   return BadRequest(Error("Subject is too long."));
        if (text.Length > 10_000)   return BadRequest(Error("Message is too long."));
        // Same rule the public text fields use: no angle brackets anywhere, so a
        // pasted signature cannot smuggle markup into a mail we send.
        if (subject.Contains('<') || subject.Contains('>') ||
            text.Contains('<')    || text.Contains('>'))
            return BadRequest(Error("Text fields contain invalid characters."));

        // ── Resolve the recipient from the LEAD, never from the request body ──
        string recipient;
        Guid?  supplierId = null;

        if (body.SupplierId is { } wanted)
        {
            // Only a provider we actually wrote to about THIS lead.
            var outreach = await Db.ProviderOutreaches
                .Where(o => o.DemandLeadId == id && o.SupplierId == wanted)
                .OrderByDescending(o => o.SentAt)
                .FirstOrDefaultAsync();
            if (outreach is null)
                return BadRequest(Error("That provider was not contacted for this request."));

            // The address on the supplier row today, falling back to the one the
            // outreach actually went to — a contact email may have been corrected
            // since, and the current one is the one that will reach them.
            var supplier = await Db.Suppliers.FindAsync(wanted);
            recipient = (supplier?.ContactEmail ?? "").Trim() is { Length: > 0 } current
                ? current
                : outreach.SentTo.Trim();
            if (recipient.Length == 0)
                return BadRequest(Error("That provider has no contact email."));
            supplierId = wanted;
        }
        else
        {
            recipient = (lead.Email ?? "").Trim();
            if (recipient.Length == 0)
                return BadRequest(Error("This request has no customer email."));
        }

        var message = new LeadMessage
        {
            Id           = Guid.NewGuid(),
            DemandLeadId = lead.Id,
            SupplierId   = supplierId,
            SentTo       = recipient,
            Subject      = subject,
            Body         = text,
            SentAt       = DateTime.UtcNow,
            SentByUserId = User.GetUserId(),
        };
        Db.LeadMessages.Add(message);

        Audit("lead.message_sent", User.GetUserId().ToString(), lead.Id.ToString(),
              $"To: {recipient}{(supplierId is null ? " (customer)" : " (provider)")}");

        // Save BEFORE queueing, the house rule everywhere in this codebase:
        // Hangfire commits the job on its own connection, so a failed save must
        // never leave a sent message with no record of having been sent.
        await Db.SaveChangesAsync();

        emailQueue.EnqueueEmail(
            recipient, subject, text,
            htmlBody: null, replyTo: await OpsInbox.ResolveAsync(Db));

        return Ok(MapLeadMessage(message));
    }

    /// <summary>Everything an operator sent by hand about this lead, newest first.</summary>
    [HttpGet("leads/{id:guid}/messages")]
    public async Task<IActionResult> GetLeadMessages(Guid id)
    {
        var rows = await Db.LeadMessages
            .AsNoTracking()
            .Where(m => m.DemandLeadId == id)
            .OrderByDescending(m => m.SentAt)
            .ToListAsync();
        return Ok(rows.Select(MapLeadMessage));
    }

    private static object MapLeadMessage(LeadMessage m) => new
    {
        id         = m.Id,
        supplierId = m.SupplierId,
        sentTo     = m.SentTo,
        subject    = m.Subject,
        body       = m.Body,
        sentAt     = m.SentAt,
    };
}

public record UpdateLeadRequest(
    string? Status = null,
    string? AdminNotes = null,
    // Request-field corrections the admin can make to the customer's submission.
    // All optional — a null/omitted field is left unchanged.
    string? Name = null,
    string? Email = null,
    string? Phone = null,
    string? Category = null,
    string? City = null,
    string? ToCity = null,
    // A string (not DateTime?): "" JSON-binds fine and means "clear", matching the
    // empty-clears convention of the other editable fields (DateTime? can't bind "").
    string? NeedDate = null,
    string? Details = null);

/// <summary>
/// POST /admin/leads/{id}/messages. <c>SupplierId</c> null = send to the
/// CUSTOMER; set = send to that provider, which is accepted only if they were
/// actually contacted for this lead. There is deliberately no recipient-address
/// field — see the endpoint.
/// </summary>
public record SendLeadMessageRequest(
    string? Subject = null,
    string? Body = null,
    Guid? SupplierId = null);
