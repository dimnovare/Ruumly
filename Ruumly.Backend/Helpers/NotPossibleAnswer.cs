namespace Ruumly.Backend.Helpers;

/// <summary>
/// Recognises a quote-form availability answer that is really a "no".
///
/// The outreach email has told providers since 2026-08 that a short "ei ole
/// võimalik" is a perfectly good answer — and some of them give it in the one
/// place they can see, the price form's availability field, with whatever number
/// happens to be in the price box. Treated as a price, that becomes an offer the
/// provider has refused (AKM Rent, Keila, 2026-11-27).
///
/// WHOLE-FIELD match only. "Ei ole võimalik 1.12, 2.12 sobib" is a quote with
/// another date in it, and must stay one; only an answer that says nothing BUT
/// no is a decline. The phrases are the email's own wording in all five
/// languages plus the plain short forms people type.
/// </summary>
public static class NotPossibleAnswer
{
    private static readonly HashSet<string> Phrases = new(StringComparer.OrdinalIgnoreCase)
    {
        // et
        "ei ole võimalik", "ei ole voimalik", "pole võimalik", "pole voimalik",
        "kahjuks ei ole võimalik", "kahjuks pole võimalik", "kahjuks ei saa", "kahjuks ei",
        "ei saa", "ei sobi", "ei ole vaba", "pole vaba", "ei",
        // en
        "not possible", "sorry, not possible", "unfortunately not possible", "not available",
        "unavailable", "cannot", "can't", "no",
        // ru
        "не получится", "к сожалению, не получится", "невозможно", "не можем", "не сможем", "нет",
        // lv
        "nav iespējams", "diemžēl nav iespējams", "nevaram", "nav pieejams", "nē",
        // lt
        "negalime", "deja, negalime", "neįmanoma", "negaliu", "ne",
    };

    public static bool Matches(string? availability)
    {
        if (string.IsNullOrWhiteSpace(availability)) return false;
        var text = availability.Trim()
            .Trim('"', '\'', '„', '“', '”', '«', '»', '.', '!', ' ')
            .Replace('\u2019', '\'');
        // Collapse inner whitespace so "ei  ole võimalik" still matches.
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return Phrases.Contains(text);
    }
}
