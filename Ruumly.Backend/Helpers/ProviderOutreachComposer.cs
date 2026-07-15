using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;

namespace Ruumly.Backend.Helpers;

public static class ProviderOutreachComposer
{
    public static ProviderOutreachMessage Compose(DemandLead lead, Supplier supplier)
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
        var body = $"{t.OutreachGreeting}\n\n"
                 + $"{t.OutreachBody(category, route, details, date)}\n\n"
                 + $"{t.OutreachAsk}\n\n{t.OutreachSignature}";
        return new(language, t.OutreachSubject(category, route), body);
    }
}
