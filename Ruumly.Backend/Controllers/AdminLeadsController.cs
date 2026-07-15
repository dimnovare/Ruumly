using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Constants;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Controllers;

[Route("api/admin")]
public class AdminLeadsController(RuumlyDbContext db) : AdminBaseController(db)
{
    /// <summary>
    /// Admin lead queue. All filters are optional and case-insensitive
    /// (null/blank = ignore): <paramref name="status"/>, <paramref name="source"/>
    /// (e.g. "concierge" for the demand funnel, "routed"/"notify-interest" for the
    /// rest), <paramref name="category"/> (a ServiceCategories slug or "any"), and
    /// <paramref name="city"/>. Default order is newest-first; set
    /// <paramref name="needsResponse"/>=true to get the SLA view — only untouched
    /// New leads (ContactedAt == null), oldest-first, so the oldest un-worked
    /// request is at the top of the queue.
    /// </summary>
    [HttpGet("leads")]
    public async Task<IActionResult> GetLeads(
        [FromQuery] string? status = null,
        [FromQuery] string? source = null,
        [FromQuery] string? category = null,
        [FromQuery] string? city = null,
        [FromQuery] bool needsResponse = false,
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

        // SLA / needs-response view: only genuinely un-worked leads, oldest first.
        if (needsResponse)
            query = query.Where(d => d.Status == DemandLeadStatus.New && d.ContactedAt == null);

        var total = await query.CountAsync();
        var ordered = needsResponse
            ? query.OrderBy(d => d.CreatedAt)            // oldest uncontacted first
            : query.OrderByDescending(d => d.CreatedAt); // newest first (default)
        var leads = await ordered
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(d => new
            {
                d.Id,
                d.Name,
                d.Email,
                d.Phone,
                d.City,
                category     = d.Category.ToString().ToLower(),
                d.Query,
                d.Language,
                d.CreatedAt,
                status       = d.Status.ToString().ToLower(),
                d.AdminNotes,
                // Concierge intake context (null for legacy/routed leads)
                d.ToCity,
                d.NeedDate,
                d.Details,
                d.Source,
                d.ContactedAt,
                // Routing + quote (null for generic demand-capture leads)
                d.SupplierId,
                supplierName = d.SupplierId == null
                    ? null
                    : Db.Suppliers.Where(s => s.Id == d.SupplierId).Select(s => s.Name).FirstOrDefault(),
                d.ListingId,
                d.QuotedPrice,
                d.QuotedAt,
                d.ProviderNotes,
            })
            .ToListAsync();

        return Ok(new { total, page, limit, items = leads });
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
            medianFirstResponseMinutes,
        });
    }
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
