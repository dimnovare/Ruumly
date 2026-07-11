using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// The one place that moves a <see cref="DemandLead"/> between statuses.
/// Used by the admin CRM (manual status changes) and the offer loop
/// (send offer → Quoted, send outreach → Contacted, choose → Converted).
/// </summary>
public static class DemandLeadLifecycle
{
    /// <summary>
    /// A "genuine contact" status: the admin actually reached the customer and
    /// moved the request forward (Contacted / Quoted / Converted). New is
    /// pre-contact; Dismissed and Unmatched are closures (spam, or no partner
    /// could serve it) — NOT contact. Only genuine-contact states may stamp
    /// <see cref="DemandLead.ContactedAt"/> and feed the response-time and
    /// contact-rate metrics, otherwise dismissing spam would make the ops team
    /// look artificially fast and inflate the contact rate.
    /// </summary>
    public static bool IsGenuineContact(DemandLeadStatus status) =>
        status is DemandLeadStatus.Contacted
               or DemandLeadStatus.Quoted
               or DemandLeadStatus.Converted;

    /// <summary>
    /// Sets the status and, on the FIRST genuine-contact transition, stamps
    /// <see cref="DemandLead.ContactedAt"/> once — it powers the median-first-
    /// response ops metric and is never overwritten by later transitions.
    /// Moves into Dismissed/Unmatched (or back to New) never stamp it.
    /// </summary>
    public static void MoveTo(DemandLead lead, DemandLeadStatus status)
    {
        lead.Status = status;
        if (lead.ContactedAt is null && IsGenuineContact(status))
            lead.ContactedAt = DateTime.UtcNow;
    }
}
