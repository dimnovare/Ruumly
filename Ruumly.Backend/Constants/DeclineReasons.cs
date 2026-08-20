namespace Ruumly.Backend.Constants;

/// <summary>
/// Why a provider said NO to a request — one slug per decline, stored on
/// <see cref="Models.ProviderOutreach.DeclineReason"/>.
///
/// The catalogue is the four answers the ops inbox actually receives when a
/// provider bothers to say more than "ei saa": wrong place, wrong day, wrong
/// trade, wrong size. Each routes ops differently — wrong place/trade means the
/// DIRECTORY ROW is mis-filed and every future fan-out to them is wasted;
/// wrong day/size is about this one lead and the provider stays a good
/// candidate for the next.
///
/// A SINGLE slug, not a list like <see cref="InfoRequestReasons"/>: a decline
/// is one decision, and asking a provider who is leaving to itemise their exit
/// costs answers. Stored as a plain string, never an enum name, for the same
/// reason InfoRequestReasons documents — a retired slug must degrade to
/// "no reason given", not make rows unreadable.
///
/// Labels are ENGLISH ONLY: they go into the internal ops alert. The
/// provider-facing wording lives in the frontend, in their language.
/// </summary>
public static class DeclineReasons
{
    /// <summary>Outside the area they serve — the directory row's geography is likely wrong.</summary>
    public const string WrongArea = "wrong_area";

    /// <summary>No capacity on/around the requested date — good candidate next time.</summary>
    public const string NoCapacity = "no_capacity";

    /// <summary>Not a service they offer — the directory row's service list is likely wrong.</summary>
    public const string NotOurService = "not_our_service";

    /// <summary>Job too small (or otherwise not worth it) — good candidate for bigger work.</summary>
    public const string TooSmall = "too_small";

    /// <summary>Anything else — the note carries it.</summary>
    public const string Other = "other";

    /// <summary>Every reason, in the order the quote page shows them.</summary>
    public static readonly IReadOnlyList<string> All =
        [WrongArea, NoCapacity, NotOurService, TooSmall, Other];

    private static readonly IReadOnlyDictionary<string, string> Labels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [WrongArea]     = "Outside their area (check the row's city/coverage)",
            [NoCapacity]    = "No capacity for that date",
            [NotOurService] = "Not a service they offer (check the row's services)",
            [TooSmall]      = "Too small / not worth it for them",
            [Other]         = "Other (see their note)",
        };

    private static readonly HashSet<string> Known = new(All, StringComparer.Ordinal);

    /// <summary>
    /// A stored or submitted slug, validated: unknown/blank collapses to null
    /// ("no reason given"), which is a complete decline all the same.
    /// </summary>
    public static string? Normalize(string? slug)
    {
        var trimmed = slug?.Trim();
        return trimmed is not null && Known.Contains(trimmed) ? trimmed : null;
    }

    /// <summary>English label for the internal ops alert.</summary>
    public static string OpsLabel(string slug) =>
        Labels.TryGetValue(slug, out var label) ? label : slug;
}
