using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// The one place that moves a <see cref="DemandLead"/> between statuses.
/// Used by the admin CRM (manual status changes) and the offer loop
/// (send offer → Quoted, send outreach → Contacted).
/// </summary>
public static class DemandLeadLifecycle
{
    /// <summary>
    /// Sets the status and stamps the first-response timestamp: any move out
    /// of New records <see cref="DemandLead.ContactedAt"/> once — it powers
    /// the median-first-response ops metric and is never overwritten by
    /// later transitions.
    /// </summary>
    public static void MoveTo(DemandLead lead, DemandLeadStatus status)
    {
        lead.Status = status;
        if (status != DemandLeadStatus.New && lead.ContactedAt is null)
            lead.ContactedAt = DateTime.UtcNow;
    }
}
