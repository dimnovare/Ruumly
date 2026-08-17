using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Constants;

/// <summary>
/// Canonical service-category slugs: every concrete <see cref="DemandLeadCategory"/>
/// name lower-cased (the Any wildcard excluded). One source of truth shared by the
/// concierge intake (POST /api/leads/request) and the provider directory
/// (Supplier.ServiceTypesJson), so a category added to the enum is automatically
/// accepted everywhere.
///
/// TWO AUDIENCES, TWO SETS (2026-08 founder decision):
///   • <see cref="BySlug"/> — the STORAGE catalogue. What a supplier row may carry
///     in ServiceTypesJson and what a historical DemandLead may already be tagged
///     with. Nothing is ever removed from it.
///   • <see cref="ConsumerSlugs"/> — the SALES catalogue. What a visitor may pick
///     in the concierge intake and public search, and what the sitemap advertises.
/// </summary>
public static class ServiceCategories
{
    /// <summary>slug → enum, e.g. "vanrental" → DemandLeadCategory.VanRental.</summary>
    public static readonly IReadOnlyDictionary<string, DemandLeadCategory> BySlug =
        Enum.GetValues<DemandLeadCategory>()
            .Where(c => c != DemandLeadCategory.Any)
            .ToDictionary(c => c.ToString().ToLowerInvariant(), c => c);

    public const string Packing   = "packing";
    public const string Moving    = "moving";
    public const string Insurance = "insurance";

    /// <summary>
    /// RETAINED FOR DATA, NOT FOR SALE. These slugs stay fully valid on the supply
    /// side — a mover that also packs keeps its "packing" tag (useful supplier
    /// metadata, and it is what powers the packing add-on) — but are never offered
    /// to a consumer as a top-level service.
    ///
    /// Why (market research across EE/LV/LT, 2026-08):
    ///   • packing   — never sold standalone anywhere in the Baltics; it is always a
    ///     line item inside a moving company's offer. Lithuania's national directory
    ///     even lists the same two companies under "pakavimo paslaugos" as under its
    ///     general moving category. Advertising it as bookable sends the customer to
    ///     a dead end with nobody to quote.
    ///   • insurance — in this market that means CMR carrier-liability cover sold B2B
    ///     to hauliers, not goods-in-transit cover a household can buy. Wrong customer
    ///     entirely, and the thinnest vertical we had (one Estonian city).
    ///
    /// DO NOT delete the matching <see cref="DemandLeadCategory"/> members or purge
    /// stored ServiceTypesJson values: production DemandLead rows already carry these
    /// categories and some columns persist enum NAMES, so removing a member breaks
    /// reading historical leads. This is a presentation/routing change only.
    /// </summary>
    public static readonly IReadOnlySet<string> RetainedNotSoldSlugs =
        new HashSet<string>(StringComparer.Ordinal) { Packing, Insurance };

    /// <summary>
    /// Slugs a consumer may actually choose: the public concierge intake, public
    /// search filters and the sitemap's service×city hubs all read from this.
    /// </summary>
    public static readonly IReadOnlyList<string> ConsumerSlugs =
        BySlug.Keys.Where(s => !RetainedNotSoldSlugs.Contains(s)).ToList();

    /// <summary>True when a visitor is allowed to pick this slug as a service.</summary>
    public static bool IsConsumerSlug(string? slug) =>
        slug is not null && BySlug.ContainsKey(slug) && !RetainedNotSoldSlugs.Contains(slug);

    /// <summary>
    /// What a slug arriving on a PUBLIC surface should be served as:
    ///   consumer slug → itself
    ///   "packing"     → "moving"  (packing is an add-on line inside a mover's offer)
    ///   "insurance"   → null      (no consumer equivalent — generic search / Any lead)
    ///   unknown slug  → null
    /// Callers that must tell "retired" apart from "never existed" check
    /// <see cref="BySlug"/> first — the city hubs for both retired slugs are live and
    /// indexed, so those URLs must resolve to something real rather than dead-end.
    /// </summary>
    public static string? PublicAliasFor(string? slug) => slug switch
    {
        Packing                  => Moving,
        Insurance                => null,
        _ when IsConsumerSlug(slug) => slug,
        _                        => null,
    };

    /// <summary>Slug for a concrete category ("cleaning"); Any → "any".</summary>
    public static string SlugFor(DemandLeadCategory category) =>
        category.ToString().ToLowerInvariant();

    // ─── Add-on markers carried in DemandLead.Query ───────────────────────────
    //
    // HOW THE PACKING ADD-ON TRAVELS FROM INTAKE TO THE PROVIDER EMAIL.
    //
    // A "packing" request is routed to a Moving lead (see PublicAliasFor), so the
    // lead's own Category can no longer tell anyone that packing was asked for.
    // That intent is written as a marker into the Query machine summary — NOT into
    // Details — and ProviderOutreachComposer reads it back at compose time to
    // render a properly LOCALIZED packing line.
    //
    // Why Query and not Details:
    //   • Details is printed VERBATIM to the provider (and is the customer's own
    //     free text). An English ops note in the middle of an Estonian cold email
    //     is exactly the credibility leak this marker exists to avoid.
    //   • Query is English by existing convention, is what the admin list view
    //     shows, and is already where the routing is spelled out.
    //   • No migration: both columns already exist.
    //
    // Why it survives the clamps: the intake builds Query as
    // "concierge: {categories}{markers} | {city}[→{toCity}] | {date}" and clamps
    // the TAIL at 500 chars, so the marker — always in the first segment, at most
    // a few dozen characters in — can never be truncated away. The 2000-char
    // Details clamp is now irrelevant to the signal, and the customer's own words
    // are no longer pushed out to make room for an ops note.

    /// <summary>Prefix the concierge intake stamps on every Query it writes.</summary>
    public const string ConciergeQueryPrefix = "concierge: ";

    /// <summary>Appended to the Query category summary when packing was asked for on a lead routed elsewhere.</summary>
    public const string PackingAddOnMarker = "+packing-addon";

    /// <summary>Appended to the Query category summary when insurance was asked for; admin-only, never shown to a provider.</summary>
    public const string InsuranceAskedMarker = "+insurance-asked";

    /// <summary>
    /// Appended when the visitor explicitly answered "my date is flexible"
    /// rather than leaving the date blank.
    ///
    /// Those are different facts and were indistinguishable: the funnel requires
    /// a date for moving/trailer/van/cleaning and offers "flexible" as the
    /// escape, but the answer was collected in the browser and never sent. So a
    /// provider was told the customer "did not specify a date, we will confirm
    /// it" when the customer had in fact answered the question — and ops could
    /// not tell a considered "any day suits me" from an abandoned field.
    /// </summary>
    public const string DateFlexibleMarker = "+date-flexible";

    /// <summary>
    /// True when this lead's Query carries the packing add-on marker in the
    /// segment the intake actually wrote.
    ///
    /// Deliberately strict: other lead sources put RAW CUSTOMER TEXT in Query
    /// (SupportController's routed quote-request leads store the visitor's
    /// message there), and even a concierge Query interpolates the customer's
    /// city after the first " | ". Requiring the concierge prefix AND restricting
    /// the search to the leading category segment means a visitor cannot type
    /// "+packing-addon" into a free-text field and inject a line into provider mail.
    /// </summary>
    public static bool HasPackingAddOn(string? query) =>
        HasMarker(query, PackingAddOnMarker);

    /// <summary>
    /// True when the visitor explicitly said their date is flexible, as opposed
    /// to simply not answering. Same strictness as <see cref="HasPackingAddOn"/>.
    /// </summary>
    public static bool HasFlexibleDate(string? query) =>
        HasMarker(query, DateFlexibleMarker);

    /// <summary>
    /// Whether the intake wrote <paramref name="marker"/> into the leading
    /// category segment of a concierge Query.
    /// </summary>
    private static bool HasMarker(string? query, string marker)
    {
        if (string.IsNullOrEmpty(query) ||
            !query.StartsWith(ConciergeQueryPrefix, StringComparison.Ordinal))
            return false;

        var head      = query[ConciergeQueryPrefix.Length..];
        var separator = head.IndexOf(" | ", StringComparison.Ordinal);
        if (separator >= 0) head = head[..separator];

        return head.Contains(marker, StringComparison.Ordinal);
    }

    /// <summary>
    /// The consumer services the visitor actually selected, recovered from the
    /// Query machine summary in the order the intake wrote them.
    ///
    /// A lead carries ONE Category column, so a visitor who picks several services
    /// — which the intake copy explicitly invites them to do — collapses to Any and
    /// the column stops describing the request. This leading segment is then the
    /// only surviving record of the ask, and reading it back is what lets a
    /// "moving + warehouse" request still reach movers AND storage providers
    /// instead of nobody.
    ///
    /// As strict as <see cref="HasPackingAddOn"/> and for the same reason: only a
    /// Query carrying the concierge prefix qualifies, and only the segment before
    /// the first " | " is read — everything after it interpolates the customer's
    /// own city, so no visitor can type a service list into a free-text field and
    /// choose who gets emailed. Keeping only consumer slugs also drops the "any"
    /// placeholder and the +packing-addon / +insurance-asked markers.
    /// </summary>
    public static List<string> SelectedSlugs(string? query)
    {
        if (string.IsNullOrEmpty(query) ||
            !query.StartsWith(ConciergeQueryPrefix, StringComparison.Ordinal))
            return [];

        var head      = query[ConciergeQueryPrefix.Length..];
        var separator = head.IndexOf(" | ", StringComparison.Ordinal);
        if (separator >= 0) head = head[..separator];

        return head
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => IsConsumerSlug(token))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Normalizes a raw list of service-type slugs (trim + lowercase + dedupe)
    /// and keeps only the ones that are valid <see cref="BySlug"/> categories.
    /// Unknown slugs are silently dropped so a self-serve applicant's free-form
    /// input can be persisted safely into Supplier.ServiceTypesJson without
    /// rejecting the whole application.
    ///
    /// SUPPLY side — validates against the full storage catalogue on purpose, so
    /// "packing"/"insurance" are still accepted and stored. They are supplier
    /// metadata (which movers also pack), not things we sell; see
    /// <see cref="RetainedNotSoldSlugs"/>.
    /// </summary>
    public static List<string> NormalizeAndValidate(IEnumerable<string>? raw) =>
        (raw ?? [])
            .Select(t => t?.Trim().ToLowerInvariant() ?? "")
            .Where(t => t.Length > 0 && BySlug.ContainsKey(t))
            .Distinct()
            .ToList();

    /// <summary>
    /// Normalizes the service types an ADMIN is attaching to a supplier by hand,
    /// and says why when the input is unusable.
    ///
    /// Distinct from <see cref="NormalizeAndValidate"/>, which silently drops junk:
    /// a hand-typed partner is the one place where dropping input silently is the
    /// whole bug. A supplier whose ServiceTypesJson ends up empty can never be
    /// returned by ProviderCandidateFinder for any category — it looks perfectly
    /// fine in admin and is simply invisible — so a bad list must be refused loudly
    /// instead of persisted as [].
    ///
    /// Rules:
    ///   • unknown slug                → refused, naming it
    ///   • packing / insurance         → refused as a NEW choice (see
    ///     <see cref="RetainedNotSoldSlugs"/>), but PRESERVED when the supplier
    ///     already carries it, so editing a mover that also packs never silently
    ///     strips its packing tag and the admin never has to re-send it
    ///   • no consumer slug left       → refused (an empty list is the dead end)
    /// </summary>
    /// <param name="raw">Slugs supplied by the caller.</param>
    /// <param name="alreadyStored">Slugs currently on the supplier (parsed from ServiceTypesJson).</param>
    public static bool TryNormalizeOfferable(
        IEnumerable<string>? raw,
        IEnumerable<string>? alreadyStored,
        out List<string> normalized,
        out string? error)
    {
        normalized = [];
        error      = null;

        var stored = new HashSet<string>(
            (alreadyStored ?? []).Select(s => s?.Trim().ToLowerInvariant() ?? ""),
            StringComparer.Ordinal);

        var offered = new List<string>();
        foreach (var slug in (raw ?? []).Select(t => t?.Trim().ToLowerInvariant() ?? "")
                                        .Where(t => t.Length > 0)
                                        .Distinct())
        {
            if (!BySlug.ContainsKey(slug))
            {
                error = $"Unknown service type '{slug}' (allowed: {string.Join("|", ConsumerSlugs)}).";
                return false;
            }
            if (RetainedNotSoldSlugs.Contains(slug))
            {
                // Kept below via the preserved set — but only if it was already there.
                if (!stored.Contains(slug))
                {
                    error = $"Service type '{slug}' is retained for existing data only and " +
                            $"cannot be added (allowed: {string.Join("|", ConsumerSlugs)}).";
                    return false;
                }
                continue;
            }
            offered.Add(slug);
        }

        if (offered.Count == 0)
        {
            error = $"At least one service type is required " +
                    $"(allowed: {string.Join("|", ConsumerSlugs)}).";
            return false;
        }

        // Retained-not-sold slugs already on the row survive the write untouched.
        normalized = offered
            .Concat(stored.Where(RetainedNotSoldSlugs.Contains))
            .Distinct()
            .ToList();
        return true;
    }

    /// <summary>
    /// Tolerant parse of a Supplier.ServiceTypesJson value (JSON array of slugs).
    /// Null, empty, or malformed input yields an empty list — directory rows must
    /// never fail a request because one imported record carries bad JSON.
    /// </summary>
    public static List<string> ParseServiceTypes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }
}
