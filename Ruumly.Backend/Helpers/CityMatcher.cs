using System.Globalization;
using System.Text;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// Decides whether the city a CUSTOMER typed is the same place as the city on a
/// PROVIDER row. Everything downstream hangs off this: ProviderCandidateFinder
/// derives its geographic anchor only from rows that match, and with no anchor
/// the nearby search returns an empty set — so a miss here means the auto
/// fan-out silently emails nobody, and the radius widening (25 → 50 → 100 km)
/// cannot rescue it because every pass re-derives the same null anchor.
///
/// It used to be a plain OrdinalIgnoreCase string compare. Case was the only
/// thing it forgave, and against production data on 2026-08-13 that meant:
///
///   "Таллинн"   → 0 rows   (the RU placeholder in Ruumly's OWN intake form)
///   "Harjumaa"  → 0 rows   (the region the intake hint invites people to type)
///   "Riga"      → 0 rows   (the same city as the stored "Rīga")
///   "Lasnamäe"  → 0 rows   (a Tallinn district)
///
/// A Russian-speaking Tallinn customer following the placeholder reached no
/// providers at all, and nothing on the customer's screen said so.
///
/// Three forgiving steps, in order. Deliberately NOT fuzzy matching: a false
/// positive routes somebody's move to the wrong end of the country, which is
/// worse than the honest miss this replaces.
/// </summary>
public static class CityMatcher
{
    /// <summary>
    /// Names for the same place that no amount of character folding will join:
    /// other scripts, and the region a customer names when they mean its city.
    ///
    /// Region → city is a judgement, not a fact. "Harjumaa" resolves to Tallinn
    /// because someone typing the county in a moving request means greater
    /// Tallinn, and the radius search then reaches the neighbours anyway. Only
    /// unambiguous cases belong here.
    /// </summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        // Cyrillic — Ruumly ships Russian as a first-class language and its own
        // intake placeholder is Cyrillic, so these are ordinary input.
        ["таллинн"] = "tallinn", ["таллин"] = "tallinn",
        ["тарту"]   = "tartu",   ["нарва"]  = "narva",
        ["пярну"]   = "parnu",   ["вильнюс"] = "vilnius",
        ["рига"]    = "riga",    ["каунас"] = "kaunas",
        ["клайпеда"] = "klaipeda", ["даугавпилс"] = "daugavpils",
        ["юрмала"]  = "jurmala", ["лиепая"] = "liepaja",
        // Estonian counties and Tallinn districts → the city they belong to.
        ["harjumaa"] = "tallinn", ["harju maakond"] = "tallinn",
        ["lasnamae"] = "tallinn", ["mustamae"] = "tallinn",
        ["oismae"]   = "tallinn", ["kristiine"] = "tallinn",
        ["pohja-tallinn"] = "tallinn", ["kesklinn"] = "tallinn",
        ["nomme"]    = "tallinn", ["pirita"] = "tallinn", ["haabersti"] = "tallinn",
        ["tartumaa"] = "tartu",   ["parnumaa"] = "parnu",
        // Latvian / Lithuanian equivalents.
        ["rigas rajons"] = "riga", ["vilniaus rajonas"] = "vilnius",
        ["kauno rajonas"] = "kaunas", ["klaipedos rajonas"] = "klaipeda",
        ["klaipedos r"] = "klaipeda",
    };

    /// <summary>
    /// True when both names denote the same place. Blank on either side is
    /// never a match — an empty lead city must not sweep in every provider.
    /// </summary>
    public static bool IsSameCity(string? a, string? b)
    {
        var left  = Normalize(a);
        var right = Normalize(b);
        return left.Length > 0 && left == right;
    }

    /// <summary>
    /// Lower-cased, diacritic-stripped, punctuation-trimmed, alias-resolved.
    /// Exposed so callers can group or index by the same key this compares on.
    /// </summary>
    public static string Normalize(string? city)
    {
        if (string.IsNullOrWhiteSpace(city)) return "";

        var trimmed = city.Trim().ToLowerInvariant();

        // "Klaipėdos r." and "Klaipėdos r" are the same answer.
        trimmed = trimmed.TrimEnd('.', ',', ' ');

        // Strip combining marks: ä→a, õ→o, ī→i, ė→e, ž→z. Handles Rīga/Riga and
        // Lasnamäe/Lasnamae without a table entry each.
        var decomposed = trimmed.Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                folded.Append(ch);
        }

        // Estonian õ and the Nordic ø/å/æ do not decompose to a bare letter, so
        // they need spelling out explicitly.
        var key = folded.ToString().Normalize(NormalizationForm.FormC)
            .Replace("õ", "o").Replace("ø", "o").Replace("å", "a")
            .Replace("æ", "ae").Replace("ß", "ss");

        // Collapse internal whitespace so "harju  maakond" resolves too.
        key = string.Join(' ', key.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return Aliases.TryGetValue(key, out var canonical) ? canonical : key;
    }
}
