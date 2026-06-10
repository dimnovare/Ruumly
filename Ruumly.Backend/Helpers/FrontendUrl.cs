namespace Ruumly.Backend.Helpers;

public static class FrontendUrl
{
    private static readonly HashSet<string> SupportedLanguages =
        ["et", "en", "ru", "lv", "lt"];

    public static string Localized(string? appUrl, string? language, string pathAndQuery)
    {
        var lang = language is not null && SupportedLanguages.Contains(language)
            ? language
            : "et";

        return $"{appUrl?.TrimEnd('/')}/{lang}/{pathAndQuery.TrimStart('/')}";
    }
}
