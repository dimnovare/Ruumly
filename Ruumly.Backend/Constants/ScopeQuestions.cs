using System.Text.Json;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Constants;

/// <summary>
/// One scoping question the concierge intake asks as a row of one-tap chips.
/// <paramref name="Options"/> is how many chips it has — answers are 1-based
/// POSITIONS, never free text, so the wording can be retranslated (or fixed)
/// without rewriting stored rows.
/// </summary>
/// <param name="Id">Matches the frontend's <c>SCOPE_QUESTIONS</c> id exactly (camelCase, ordinal).</param>
/// <param name="Service">The <see cref="ServiceCategories"/> slug this question belongs to.</param>
/// <param name="Options">Chip count. The LAST option always means "not sure".</param>
public sealed record ScopeQuestion(string Id, string Service, int Options);

/// <summary>One answered question, already validated against the catalogue.</summary>
public sealed record ScopeAnswer(string QuestionId, int Option);

/// <summary>
/// The scoping questions the intake asks, as the BACKEND understands them.
///
/// WHY THIS EXISTS AT ALL. The funnel already asks "how big is the home",
/// "floor and lift", "how long do you need it" as chips — and then threw the
/// structure away: it rendered each answer to a sentence in the CUSTOMER's
/// language and glued them into the free-text Details blob, which
/// ProviderOutreachComposer prints verbatim. So a Russian-speaking customer's
/// answers were pasted, in Russian, into an Estonian mover's email, and nothing
/// was queryable — "show me the moves with no lift" was not a question the
/// admin could ask. Storing the POSITION and resolving the wording at compose
/// time, in the recipient's language, is what fixes both.
///
/// SHAPE OF THE CATALOGUE. A flat, ordered list rather than a
/// service→questions map:
///   • Adding a question is ONE entry here plus its label/option strings in
///     EmailTranslations. Phase 1 is largely about adding questions (two-ended
///     access, tow capability, driver-or-not, cleaning add-ons), so the cost of
///     one more has to stay at one line.
///   • The order IS the render order of the provider email. Reading it from a
///     fixed list means two leads with the same answers always produce the same
///     email — the alternative is JSON key order, which is chosen by the
///     browser, not by us.
///
/// UNKNOWN IDS ARE DROPPED, NEVER FATAL. Both directions. On the way IN because
/// the payload comes from a browser and a stale service-worker-cached bundle
/// may still send a question we have retired. On the way OUT because a stored
/// row outlives the build that wrote it: a question removed from this list must
/// degrade to "that line is not printed", never to an exception on the outreach
/// email that carries the request itself.
///
/// DELIBERATELY NOT FILTERED BY THE LEAD'S CATEGORY. A visitor who picks
/// several services collapses to <c>DemandLeadCategory.Any</c> (the intake copy
/// invites exactly that), so "keep only the questions belonging to this lead's
/// category" would silently discard the answers of every multi-service request
/// — the ones that need scoping most. <see cref="ScopeQuestion.Service"/> is
/// there to describe a question, not to police an answer.
/// </summary>
public static class ScopeQuestions
{
    public const string WarehouseSize     = "warehouseSize";
    public const string WarehouseDuration = "warehouseDuration";
    public const string WarehouseGoods    = "warehouseGoods";
    public const string MovingSize        = "movingSize";
    public const string MovingAccess      = "movingAccess";
    public const string MovingAccessFrom  = "movingAccessFrom";
    public const string MovingAccessTo    = "movingAccessTo";
    public const string MovingHeavyItems  = "movingHeavyItems";
    public const string PackingHelp       = "packingHelp";
    public const string TrailerDuration   = "trailerDuration";
    public const string TrailerType       = "trailerType";
    public const string TrailerTow        = "trailerTow";
    public const string VanRentalDriver   = "vanrentalDriver";
    public const string VanRentalDuration = "vanrentalDuration";
    public const string VanRentalSize     = "vanrentalSize";
    public const string CleaningType      = "cleaningType";
    public const string CleaningSize      = "cleaningSize";
    public const string CleaningExtras    = "cleaningExtras";

    // Service slugs derived from the enum rather than typed as literals: a
    // question that claims a service which does not exist would be invisible
    // (it renders fine and simply never groups with anything), so the compiler
    // is the right place to catch it. Declared before All — static field
    // initializers run in textual order.
    private static readonly string Warehouse = ServiceCategories.SlugFor(DemandLeadCategory.Warehouse);
    private static readonly string Moving    = ServiceCategories.SlugFor(DemandLeadCategory.Moving);
    private static readonly string Trailer   = ServiceCategories.SlugFor(DemandLeadCategory.Trailer);
    private static readonly string VanRental = ServiceCategories.SlugFor(DemandLeadCategory.VanRental);
    private static readonly string Cleaning  = ServiceCategories.SlugFor(DemandLeadCategory.Cleaning);

    /// <summary>
    /// Every question this build understands, in the order a provider email
    /// renders them. Grouped by service, and within a service in the order the
    /// funnel asks — so the email reads like the form the customer filled in.
    ///
    /// <c>packingHelp</c> is a moving ADD-ON: optional in the funnel (it never
    /// blocks Next) but an ordinary catalogue entry here, because from the
    /// provider's side it is simply another fact about the job being priced.
    ///
    /// <c>movingAccess</c> IS RETAINED THOUGH THE FUNNEL NO LONGER ASKS IT. It
    /// was one "floor and lift" question about the move as a whole; it is now
    /// two, because a ground-floor pickup into a 5th-floor walk-up is a
    /// different price from the reverse and the single answer could not say
    /// which end it meant. Leads submitted before that split still carry it, and
    /// their outreach — resent whenever the admin fans out to another provider —
    /// must keep rendering the access answer the customer actually gave. It sits
    /// next to its two successors so the legacy render order (size → access →
    /// packing) is unchanged, and no lead ever carries both: a browser sends one
    /// shape or the other, never a mix.
    /// </summary>
    public static readonly IReadOnlyList<ScopeQuestion> All =
    [
        new(WarehouseSize,     Warehouse, 6),
        new(WarehouseDuration, Warehouse, 5),
        new(WarehouseGoods,    Warehouse, 6),
        new(MovingSize,        Moving,    6),
        new(MovingAccess,      Moving,    5),   // legacy — see the note above
        new(MovingAccessFrom,  Moving,    5),
        new(MovingAccessTo,    Moving,    5),
        new(MovingHeavyItems,  Moving,    6),
        new(PackingHelp,       Moving,    4),
        new(TrailerDuration,   Trailer,   5),
        new(TrailerType,       Trailer,   5),
        new(TrailerTow,        Trailer,   5),
        // Driver-or-not leads the van block deliberately: it is the answer that
        // decides whether the request is van rental at all or a moving job with
        // a crew, so a provider reading the email top-down learns what they are
        // being asked for before they read how long and how big.
        new(VanRentalDriver,   VanRental, 5),
        new(VanRentalDuration, VanRental, 5),
        new(VanRentalSize,     VanRental, 4),
        new(CleaningType,      Cleaning,  5),
        new(CleaningSize,      Cleaning,  5),
        new(CleaningExtras,    Cleaning,  6),
    ];

    private static readonly IReadOnlyDictionary<string, ScopeQuestion> ById =
        All.ToDictionary(q => q.Id, StringComparer.Ordinal);

    /// <summary>Catalogue position of each id — the sort key that makes the render order stable.</summary>
    private static readonly IReadOnlyDictionary<string, int> Rank =
        All.Select((q, i) => (q.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);

    /// <summary>The question with this id, or null when this build does not know it.</summary>
    public static ScopeQuestion? Find(string? id) =>
        id is not null && ById.TryGetValue(id, out var q) ? q : null;

    /// <summary>True when <paramref name="option"/> is a chip this question actually has.</summary>
    public static bool IsAnswer(string? id, int option) =>
        Find(id) is { } q && option >= 1 && option <= q.Options;

    /// <summary>
    /// Validate a submitted (or stored) set of answers: keep the ids this build
    /// knows, whose value is a whole number inside that question's chip range,
    /// and return them in catalogue order.
    ///
    /// Takes <see cref="JsonElement"/> values rather than <c>int</c> so that a
    /// junk value is DROPPED rather than rejected. Binding straight to
    /// <c>Dictionary&lt;string,int&gt;</c> would make <c>{"movingSize":null}</c>
    /// a 400 on the whole submission — the scoping answers are an extra on a
    /// request, and losing a real customer because one optional chip arrived
    /// malformed is a far worse outcome than losing that one chip.
    ///
    /// Silent dropping for the same reason <see cref="InfoRequestReasons"/>
    /// drops unknown slugs: this arrives from a public form, and one stale
    /// cached bundle must not cost the customer the request they filled in.
    /// </summary>
    public static List<ScopeAnswer> Normalize(IEnumerable<KeyValuePair<string, JsonElement>>? raw)
    {
        if (raw is null) return [];

        var kept = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (id, value) in raw)
        {
            if (value.ValueKind != JsonValueKind.Number) continue;
            // TryGetInt32 fails on 2.5 and on anything outside int range, which
            // is exactly right: an option is a chip position, not a measurement.
            if (!value.TryGetInt32(out var option)) continue;
            if (!IsAnswer(id, option)) continue;
            kept[id] = option;                       // duplicate property → last one wins
            if (kept.Count == All.Count) break;      // nothing else can be added
        }

        return kept
            .OrderBy(kv => Rank[kv.Key])
            .Select(kv => new ScopeAnswer(kv.Key, kv.Value))
            .ToList();
    }

    /// <summary>
    /// Serializes normalized answers for <c>DemandLead.ScopeJson</c>:
    /// <c>{"movingSize":2,"movingAccess":3}</c>. NULL for an empty set, so a
    /// request with no scoping answers stores nothing at all rather than an
    /// empty object that later has to be told apart from one.
    /// </summary>
    public static string? Serialize(IEnumerable<ScopeAnswer> answers)
    {
        var map = answers.ToDictionary(a => a.QuestionId, a => a.Option, StringComparer.Ordinal);
        return map.Count == 0 ? null : JsonSerializer.Serialize(map);
    }
}
