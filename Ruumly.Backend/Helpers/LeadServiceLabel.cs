using Ruumly.Backend.Constants;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// What a lead is a request FOR, named in one language, for every audience that
/// has to read it back: the provider outreach, the customer's acknowledgement,
/// and anything else that prints "you asked us for X".
///
/// It exists because a lead carries ONE Category column while the intake copy
/// explicitly invites the visitor to pick several services. A multi-service
/// request therefore collapses to <see cref="DemandLeadCategory.Any"/>, whose
/// label is a deliberately generic "Service" — which is useless in a cold email
/// and actively wrong in a receipt whose whole job is to read the request back so
/// the customer can spot their own typo.
///
/// The real pick survives in the Query machine summary the intake wrote, and
/// <see cref="ServiceCategories.SelectedSlugs"/> recovers it under strict rules
/// (concierge prefix required, leading segment only) so no visitor can inject a
/// service list through a free-text field.
///
/// Centralised rather than duplicated: the provider composer already did this
/// recovery and the customer acknowledgement did not, so the two mails describing
/// the SAME request disagreed — the stranger we cold-email got a better
/// description of the customer's request than the customer did.
/// </summary>
public static class LeadServiceLabel
{
    public static string For(EmailTranslations.EmailStrings t, DemandLead lead)
    {
        if (lead.Category != DemandLeadCategory.Any)
            return t.CategoryLabel(lead.Category);

        var selected = ServiceCategories.SelectedSlugs(lead.Query);
        return selected.Count == 0
            ? t.CategoryLabel(lead.Category)
            : string.Join(" + ", selected.Select(
                slug => t.CategoryLabel(ServiceCategories.BySlug[slug])));
    }
}
