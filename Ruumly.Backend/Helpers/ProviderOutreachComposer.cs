using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;

namespace Ruumly.Backend.Helpers;

public static class ProviderOutreachComposer
{
    /// <summary>
    /// Composes the availability-request email for a provider. When
    /// <paramref name="quoteToken"/> is supplied, the primary CTA becomes
    /// "Submit your price → {https://ruumly.eu/{lang}/quote/{token}}" (the token
    /// is per-recipient), replacing "reply to this email" as the lead action;
    /// Reply-To stays the ops inbox as the fallback. Never contains the
    /// customer's name/email/phone — the admin brokers the introduction.
    /// </summary>
    public static ProviderOutreachMessage Compose(
        DemandLead lead, Supplier supplier, string? appUrl = null, string? quoteToken = null)
    {
        var language = supplier.Country?.ToUpperInvariant() switch
        {
            "LV" => "lv",
            "LT" => "lt",
            "EE" => "et",
            _ => "en",
        };
        var t = EmailTranslations.For(language);
        var route = string.IsNullOrWhiteSpace(lead.ToCity)
            ? lead.City
            : $"{lead.City} → {lead.ToCity}";
        var date = lead.NeedDate?.ToString("yyyy-MM-dd") ?? "—";
        var details = string.IsNullOrWhiteSpace(lead.Details) ? "—" : lead.Details;
        var category = t.CategoryLabel(lead.Category);

        var cta = string.IsNullOrWhiteSpace(quoteToken)
            ? ""
            : $"{t.OutreachQuoteCta} → {FrontendUrl.Localized(appUrl, language, $"quote/{quoteToken}")}\n\n";

        var body = $"{t.OutreachGreeting}\n\n"
                 + $"{t.OutreachBody(category, route, details, date)}\n\n"
                 + $"{t.OutreachAsk}\n\n"
                 + cta
                 + $"{t.OutreachSignature}";
        return new(language, t.OutreachSubject(category, route), body);
    }
}
