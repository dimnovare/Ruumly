namespace Ruumly.Backend.Helpers;

public static class LangExtensions
{
    /// <summary>
    /// Reads the Accept-Language header from the
    /// request and returns "et", "en", or "ru".
    /// </summary>
    public static string GetLang(this HttpRequest request)
    {
        var header = request.Headers.AcceptLanguage.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(header))
            return "et";
        var primary = header.Split(',')[0].Split('-')[0].Trim().ToLower();
        return primary is "en" or "ru" ? primary : "et";
    }
}
