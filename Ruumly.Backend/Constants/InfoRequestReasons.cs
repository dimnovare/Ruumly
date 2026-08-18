using System.Text.Json;

namespace Ruumly.Backend.Constants;

/// <summary>
/// What a provider tells us is MISSING when they cannot price a request from
/// what the outreach email carried.
///
/// The list comes from the reply that made this feature necessary: Adduco, a
/// real Estonian mover, answered a live Haapsalu job with "selle info pealt
/// adekvaatset pakkumist paraku ei saa teha" and then named exactly two things —
/// whether the origin and destination were the same address, and photos. Those
/// two are the first two slugs here; the rest are the questions the ops inbox
/// has been fielding by hand.
///
/// STORED AS A JSON ARRAY OF SLUGS, in one text column, deliberately:
///
///   • A new reason has to be addable without a migration and without touching a
///     single historical row. A JSON array grows by one string; a bit-flags
///     column has to be re-interpreted and a join table needs a migration for
///     what is, in the end, six checkboxes.
///   • It must NOT persist enum names. The project has been bitten by that
///     already (see ServiceCategories / DemandLeadCategory): a column holding
///     enum NAMES makes removing or renaming a member unsafe forever, because
///     production rows stop being readable. A slug that no longer exists is a
///     string this parser simply drops — a decision we can take later, on a live
///     table, without a data migration.
///   • Reading is tolerant for the same reason <see cref="Helpers.LeadPhotos"/>
///     is: a malformed value must never be able to take down the admin lead view
///     that renders it. Unreadable reasons degrade to "none listed"; the
///     provider's own note is what actually carries the detail anyway.
///
/// The labels are ENGLISH ONLY on purpose — they are printed into the internal
/// ops alert, which is read by the ops team, never by the provider or the
/// customer. The provider-facing wording lives in the frontend, in their own
/// language; nothing here is ever mailed to a customer.
/// </summary>
public static class InfoRequestReasons
{
    /// <summary>Pictures of the load/room — the ask that started this.</summary>
    public const string Photos = "photos";

    /// <summary>Exact street addresses, and for a move whether both ends are the same one.</summary>
    public const string Address = "address";

    /// <summary>Floor, lift, stairs, parking distance — what decides the crew size.</summary>
    public const string Access = "access";

    /// <summary>A real date/time rather than "as soon as possible".</summary>
    public const string Date = "date";

    /// <summary>What is actually being moved/stored, and how much of it.</summary>
    public const string Items = "items";

    /// <summary>Anything else — the note is the payload for this one.</summary>
    public const string Other = "other";

    /// <summary>
    /// Every reason a provider may pick, in the order the form should show them.
    /// Append-only in practice: the frontend renders this order, and stored rows
    /// name slugs, so adding one is a one-line change here.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
        [Photos, Address, Access, Date, Items, Other];

    /// <summary>Upper bound on stored reasons — the whole catalogue, and no more.</summary>
    public static int MaxReasons => All.Count;

    private static readonly IReadOnlyDictionary<string, string> Labels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Photos]  = "Photos of the load",
            [Address] = "Exact addresses (and whether from/to are the same)",
            [Access]  = "Access — floor, lift, stairs, parking",
            [Date]    = "A firm date",
            [Items]   = "What is being moved/stored, and how much",
            [Other]   = "Other (see their note)",
        };

    private static readonly HashSet<string> Known = new(All, StringComparer.Ordinal);

    /// <summary>
    /// Trim, lower-case, drop anything we do not recognise, de-duplicate, and cap.
    ///
    /// Silent dropping rather than a 400 for each unknown slug: this arrives from
    /// a public form and one stale checkbox in a cached page must not cost the
    /// provider the reply they took the trouble to send. The CALLER decides what
    /// an empty result means — for the need-info endpoint, empty reasons AND an
    /// empty note is the one combination that carries no information at all and
    /// is refused there, which is the only place that refusal makes sense.
    /// </summary>
    public static List<string> Normalize(IEnumerable<string?>? raw)
    {
        if (raw is null) return [];

        var kept = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in raw)
        {
            if (kept.Count >= MaxReasons) break;
            var slug = value?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(slug) || !Known.Contains(slug)) continue;
            if (seen.Add(slug)) kept.Add(slug);
        }

        return kept;
    }

    /// <summary>Serializes normalized slugs for the ReasonsJson column.</summary>
    public static string Serialize(IEnumerable<string> reasons) =>
        JsonSerializer.Serialize(reasons.ToList());

    /// <summary>
    /// Reads ReasonsJson back. Never throws, and re-validates on the way OUT as
    /// well as in — the column is written by us today, but a slug that reaches a
    /// rendering surface should be one this build still understands.
    /// </summary>
    public static List<string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return Normalize(JsonSerializer.Deserialize<List<string?>>(json));
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Ops-inbox wording for a slug; the slug itself if it is not one we know.</summary>
    public static string OpsLabel(string slug) =>
        Labels.TryGetValue(slug, out var label) ? label : slug;
}
