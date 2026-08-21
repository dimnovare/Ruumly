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
/// <param name="Multi">
/// Tick-all-that-apply: this question accepts SEVERAL chip positions, so its
/// stored value may be an array as well as a bare number.
///
/// Two questions genuinely are lists — "anything heavy or awkward?" and
/// "windows, oven or fridge as well?" — and squeezing them into one answer cost
/// exactly the fact that decides the price. A customer with a piano AND an
/// aquarium could only reach for the catch-all chip ("several of these"), and a
/// mover pricing "several of these" is guessing at the specialist gear the whole
/// question exists to surface: a real mover refused to quote a live Haapsalu
/// move until it knew what was in it.
///
/// A DEFAULT OF FALSE IS THE SAFE DIRECTION. Marking a question Multi only ever
/// WIDENS what is accepted — a bare number stays a valid answer to it, which is
/// what every row already in production carries. A question left single-choice
/// keeps rejecting arrays exactly as it did before.
///
/// The intake also greys out combinations that contradict each other ("nothing
/// unusual" alongside a piano). That is a UI affordance and deliberately NOT
/// enforced here: the browser is not the only thing that can POST, and dropping
/// an answer a customer really gave because it disagrees with another one is a
/// worse outcome than storing a contradiction an admin can read.
/// </param>
public sealed record ScopeQuestion(string Id, string Service, int Options, bool Multi = false);

/// <summary>
/// One answered question, already validated against the catalogue: the chip
/// positions the customer picked, in ascending catalogue order.
///
/// A LIST EVEN THOUGH ALMOST EVERY QUESTION TAKES ONE ANSWER. The alternative —
/// one <c>ScopeAnswer</c> per selection — would put the same question id on two
/// rows, and every consumer downstream (the email's fact table, the quote page's
/// definition list, an admin filter) would have to re-group them to say anything
/// useful. Grouping once, here, is what keeps "one question, one line" true for
/// all of them.
/// </summary>
public sealed record ScopeAnswer(string QuestionId, IReadOnlyList<int> Options)
{
    /// <summary>The overwhelmingly common case: a question with one answer.</summary>
    public ScopeAnswer(string questionId, int option) : this(questionId, new[] { option }) { }

    /// <summary>
    /// The first selection — the compatibility view for callers that can only
    /// carry one position (the public quote DTO's legacy <c>option</c> field).
    ///
    /// Falls back to 0 rather than throwing on an empty list. Normalize never
    /// builds one, so this can only be reached by a hand-constructed answer, and
    /// 0 is a position the catalogue rejects everywhere — it degrades to "no
    /// chip" instead of to an exception on the path that carries the request.
    /// </summary>
    public int Option => Options.Count > 0 ? Options[0] : 0;

    // Value semantics, written out because the compiler's would be wrong here.
    // A positional record compares its members with EqualityComparer<T>.Default,
    // and for IReadOnlyList<int> that is REFERENCE equality — so two answers
    // naming the same chips would be unequal, silently, wherever this type is
    // compared or used as a key. A list of small ints is a value if anything is.
    public bool Equals(ScopeAnswer? other) =>
        other is not null
        && string.Equals(QuestionId, other.QuestionId, StringComparison.Ordinal)
        && Options.SequenceEqual(other.Options);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(QuestionId, StringComparer.Ordinal);
        foreach (var option in Options) hash.Add(option);
        return hash.ToHashCode();
    }
}

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
///
/// TWO STORED SHAPES, AND BOTH ARE CURRENT. <c>DemandLead.ScopeJson</c> holds
/// <c>{"movingSize":3}</c> for a single-choice question and
/// <c>{"movingHeavyItems":[2,4]}</c> for one that takes several
/// (<see cref="ScopeQuestion.Multi"/>). The bare number is not a legacy shape to
/// be migrated away from: it is what every row written before 2026-08 carries,
/// AND what a tick-all-that-apply question still writes when exactly one chip is
/// ticked. So the array in the column always means the same thing — the customer
/// really did pick more than one — and nothing about how an existing row renders
/// changed when the second shape arrived. No migration: the column is JSON text.
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
    public const string CleaningSize      = "cleaningSize";   // legacy — see All
    public const string CleaningArea      = "cleaningArea";
    public const string CleaningFrequency = "cleaningFrequency";
    public const string CleaningCondition = "cleaningCondition";
    public const string CleaningExtras    = "cleaningExtras";
    public const string CleaningHousehold = "cleaningHousehold";

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
        // Multi — a home can hold a piano AND an aquarium, and which of the two
        // it is decides the sub-crew. Still SIX positions: option 5 was the
        // single-choice era's escape hatch ("several of these") and the intake
        // has stopped offering it, but leads taken before that carry it and
        // their outreach is re-composed on every fan-out. Renumbering to close
        // the gap would silently turn those rows into a different answer.
        new(MovingHeavyItems,  Moving,    6, Multi: true),
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
        // CleaningSize IS RETAINED THOUGH THE FUNNEL NO LONGER ASKS IT — same
        // rule as MovingAccess above. Its band 3 was "70–110 m²", a 57% spread,
        // and a real Viimsi request (2026-08-20) had to be chased by hand
        // because no cleaner can price "somewhere between 70 and 110". The
        // bands are finer in CleaningArea, but positions are what the column
        // stores, so renumbering this question would silently rewrite the
        // answer every historical row gave. It stays, and outreach for an old
        // lead keeps rendering the band the customer actually picked.
        new(CleaningSize,      Cleaning,  5),
        new(CleaningArea,      Cleaning,  7),
        // How often, asked separately from WHAT KIND — because CleaningType
        // conflated the two and customers noticed before we did. The same
        // Viimsi request ticked "Regular cleaning" and then wrote "ühekordne"
        // (one-time) in the free text: the chip row had no way to say "a
        // one-off deep clean of a 4-room house", so the answer contradicted
        // itself and an operator had to write and ask which was true.
        new(CleaningFrequency, Cleaning,  5),
        // The other half of a cleaning price. A well-kept flat and one that has
        // not been touched in six months are the same square metres and not
        // remotely the same job, and nothing in the intake distinguished them.
        new(CleaningCondition, Cleaning,  4),
        // Multi, and retaining its own retired position 5 ("all three") for the
        // same reason movingHeavyItems retains its own — see the note there.
        new(CleaningExtras,    Cleaning,  6, Multi: true),
        // Pets and small children decide which products a cleaner may use, and
        // some firms price or refuse on it. Multi because a home can have both.
        new(CleaningHousehold, Cleaning,  4, Multi: true),
    ];

    private static readonly IReadOnlyDictionary<string, ScopeQuestion> ById =
        All.ToDictionary(q => q.Id, StringComparer.Ordinal);

    /// <summary>Catalogue position of each id — the sort key that makes the render order stable.</summary>
    private static readonly IReadOnlyDictionary<string, int> Rank =
        All.Select((q, i) => (q.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);

    /// <summary>The question with this id, or null when this build does not know it.</summary>
    public static ScopeQuestion? Find(string? id) =>
        id is not null && ById.TryGetValue(id, out var q) ? q : null;

    /// <summary>
    /// Validate a submitted (or stored) set of answers: keep the ids this build
    /// knows, whose value names chip positions that question actually has, and
    /// return them in catalogue order.
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

        var kept = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
        foreach (var (id, value) in raw)
        {
            if (Find(id) is not { } question) continue;
            var options = Selections(question, value);
            // No surviving chip is NO ANSWER, not a broken one — the same
            // outcome as never sending the key. That covers the empty array a
            // multi-select question sends when the visitor unticks their last
            // box, which must leave the question unanswered rather than stored
            // as an answer nothing can be rendered from.
            if (options.Count == 0) continue;
            kept[id] = options;                      // duplicate property → last one wins
            if (kept.Count == All.Count) break;      // nothing else can be added
        }

        return kept
            .OrderBy(kv => Rank[kv.Key])
            .Select(kv => new ScopeAnswer(kv.Key, kv.Value))
            .ToList();
    }

    /// <summary>
    /// The chip positions one JSON value names for one question: a bare number,
    /// or — only for a <see cref="ScopeQuestion.Multi"/> question — an array of
    /// them.
    ///
    /// AN ARRAY ON A SINGLE-CHOICE QUESTION IS DROPPED, not truncated to its
    /// first element. Nothing we ship sends one, so it can only arrive from a
    /// hand-rolled POST, and picking one of the positions a caller sent would be
    /// inventing an answer the customer never gave — on a fact a provider is
    /// about to price. "Wrong type for this question" is the same class of junk
    /// as <c>{"movingSize":"3"}</c> and is treated the same way.
    ///
    /// The other direction is deliberately NOT symmetric: a bare number is
    /// always valid, on a multi question as much as a single one. It is what
    /// every stored row carries and what a tick-all-that-apply question writes
    /// when one box is ticked.
    /// </summary>
    private static List<int> Selections(ScopeQuestion question, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number)
            return Chip(question, value) is { } single ? [single] : [];

        if (value.ValueKind != JsonValueKind.Array || !question.Multi) return [];

        var picked = new List<int>();
        foreach (var element in value.EnumerateArray())
        {
            // Junk inside the array costs that element and nothing else — the
            // same rule the object level already follows for its values.
            if (Chip(question, element) is not { } option) continue;
            if (!picked.Contains(option)) picked.Add(option);
        }

        // Chip order, not tap order. Two customers who ticked the same two boxes
        // in the opposite order have said the same thing, and the provider email
        // is composed fresh on every fan-out — so the order has to come from the
        // catalogue rather than from whichever box a thumb reached first, for
        // exactly the reason the QUESTIONS are sorted by Rank below.
        picked.Sort();
        return picked;
    }

    /// <summary>One chip position, or null when this value is not one.</summary>
    private static int? Chip(ScopeQuestion question, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number) return null;
        // TryGetInt32 fails on 2.5 and on anything outside int range, which is
        // exactly right: an option is a chip position, not a measurement.
        if (!value.TryGetInt32(out var option)) return null;
        return option >= 1 && option <= question.Options ? option : null;
    }

    /// <summary>
    /// Serializes normalized answers for <c>DemandLead.ScopeJson</c>:
    /// <c>{"movingSize":2,"movingHeavyItems":[2,4]}</c>. NULL for an empty set,
    /// so a request with no scoping answers stores nothing at all rather than an
    /// empty object that later has to be told apart from one.
    ///
    /// ONE SELECTION IS WRITTEN AS A BARE NUMBER, whether or not the question
    /// accepts several. Writing <c>[2]</c> would give the column a second shape
    /// meaning exactly what the first one already means, and would change what
    /// gets stored for questions whose answers are not changing at all. As it
    /// stands an array in the column carries information — the customer ticked
    /// more than one box — and every other row looks precisely as it always did.
    /// </summary>
    public static string? Serialize(IEnumerable<ScopeAnswer> answers)
    {
        var map = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var answer in answers)
        {
            if (answer.Options.Count == 0) continue;
            map[answer.QuestionId] = answer.Options.Count == 1
                ? answer.Options[0]
                : answer.Options.ToArray();
        }

        return map.Count == 0 ? null : JsonSerializer.Serialize(map);
    }
}
