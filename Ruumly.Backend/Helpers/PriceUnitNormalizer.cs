namespace Ruumly.Backend.Helpers;

/// <summary>
/// Turns the unit a provider typed into the unit the CUSTOMER reads.
///
/// The provider quote form is rendered in the provider's language and the price
/// unit is free text, so a Latvian company answering an English-speaking
/// customer submits "/diena" and the offer shows "60 € /diena" to someone who
/// does not read Latvian. That happened on the first real quote this system
/// ever produced (Rīga, 2026-08-13) and was corrected by hand before sending.
/// Automating offer delivery without this would ship it to customers.
///
/// Unrecognised text is returned UNCHANGED. A provider who writes something we
/// have no mapping for ("/kuu esimesed 3 kuud") means it, and mangling it would
/// be worse than leaving it in their words — the goal is to translate the
/// handful of units that are genuinely the same concept, not to police wording.
/// </summary>
public static class PriceUnitNormalizer
{
    private enum Unit { Hour, Day, Week, Month, Unknown }

    // Everything is compared lower-case with a leading slash and surrounding
    // punctuation stripped, so "/24h", "24 h" and "per 24h" all land together.
    private static readonly Dictionary<string, Unit> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        // hour
        ["h"] = Unit.Hour, ["hr"] = Unit.Hour, ["hour"] = Unit.Hour, ["tund"] = Unit.Hour,
        ["tunnis"] = Unit.Hour, ["stunda"] = Unit.Hour, ["stundā"] = Unit.Hour,
        ["val"] = Unit.Hour, ["valanda"] = Unit.Hour, ["час"] = Unit.Hour, ["ч"] = Unit.Hour,
        // day / 24h
        ["day"] = Unit.Day, ["24h"] = Unit.Day, ["24 h"] = Unit.Day, ["d"] = Unit.Day,
        ["päev"] = Unit.Day, ["ööpäev"] = Unit.Day, ["ööpäevas"] = Unit.Day,
        ["diena"] = Unit.Day, ["dienā"] = Unit.Day, ["diennakts"] = Unit.Day, ["diennaktī"] = Unit.Day,
        ["para"] = Unit.Day, ["parai"] = Unit.Day, ["diena lt"] = Unit.Day,
        ["сутки"] = Unit.Day, ["сут"] = Unit.Day, ["день"] = Unit.Day,
        // week
        ["week"] = Unit.Week, ["nädal"] = Unit.Week, ["nädalas"] = Unit.Week,
        ["nedēļa"] = Unit.Week, ["nedēļā"] = Unit.Week, ["savaitė"] = Unit.Week,
        ["savaitei"] = Unit.Week, ["неделя"] = Unit.Week, ["нед"] = Unit.Week,
        // month
        ["month"] = Unit.Month, ["kuu"] = Unit.Month, ["kuus"] = Unit.Month,
        ["mēnesis"] = Unit.Month, ["mēnesī"] = Unit.Month, ["mėn"] = Unit.Month,
        ["mėnuo"] = Unit.Month, ["mėnesiui"] = Unit.Month,
        ["месяц"] = Unit.Month, ["мес"] = Unit.Month,
    };

    private static readonly Dictionary<Unit, Dictionary<string, string>> Rendered = new()
    {
        [Unit.Hour]  = new() { ["et"] = "/tund",    ["en"] = "/hour",  ["ru"] = "/час",
                               ["lv"] = "/stundā",  ["lt"] = "/val." },
        [Unit.Day]   = new() { ["et"] = "/ööpäev",  ["en"] = "/day",   ["ru"] = "/сутки",
                               ["lv"] = "/diennaktī", ["lt"] = "/parai" },
        [Unit.Week]  = new() { ["et"] = "/nädal",   ["en"] = "/week",  ["ru"] = "/неделя",
                               ["lv"] = "/nedēļā", ["lt"] = "/savaitei" },
        [Unit.Month] = new() { ["et"] = "/kuu",     ["en"] = "/month", ["ru"] = "/месяц",
                               ["lv"] = "/mēnesī", ["lt"] = "/mėn." },
    };

    /// <summary>
    /// The unit as the customer should read it. Returns the input untouched when
    /// it is blank, unrecognised, or already in the target language.
    /// </summary>
    public static string? ToCustomerLanguage(string? providerUnit, string language)
    {
        if (string.IsNullOrWhiteSpace(providerUnit)) return providerUnit;

        var key = providerUnit.Trim().TrimStart('/', '\\', ' ').Trim();
        // "per day", "a 24h" — drop a leading preposition before matching.
        foreach (var prefix in new[] { "per ", "a ", "eest ", "par ", "už ", "в ", "за " })
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                key = key[prefix.Length..].Trim();
        }
        key = key.TrimEnd('.', ',', ' ');

        if (!Known.TryGetValue(key, out var unit)) return providerUnit;

        var lang = Rendered[unit].ContainsKey(language) ? language : "en";
        return Rendered[unit][lang];
    }
}
