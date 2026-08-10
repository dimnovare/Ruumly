namespace Ruumly.Backend.Helpers;

public static class FrontendUrl
{
    private static readonly HashSet<string> SupportedLanguages =
        ["et", "en", "ru", "lv", "lt"];

    /// <summary>
    /// Public site origin, used when no AppUrl is configured. Outbound mail must
    /// never print a bare "/et/contact": a relative path in an email body is a
    /// dead link, and the contact page is now the only alternative to replying.
    /// </summary>
    public const string DefaultOrigin = "https://ruumly.eu";

    /// <summary>
    /// The localized contact page — the one non-reply channel offered to
    /// providers since the support phone was retired (2026-08).
    /// </summary>
    public static string Contact(string? appUrl, string? language) =>
        Localized(string.IsNullOrWhiteSpace(appUrl) ? DefaultOrigin : appUrl, language, "contact");

    public static string Localized(string? appUrl, string? language, string pathAndQuery)
    {
        var lang = language is not null && SupportedLanguages.Contains(language)
            ? language
            : "et";

        return $"{appUrl?.TrimEnd('/')}/{lang}/{pathAndQuery.TrimStart('/')}";
    }
}
