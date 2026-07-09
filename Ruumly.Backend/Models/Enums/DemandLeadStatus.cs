namespace Ruumly.Backend.Models.Enums;

public enum DemandLeadStatus
{
    New,
    Contacted,
    Converted,
    Dismissed,
    // Appended last (stable int value). The partner has sent the customer a price.
    Quoted,
    // Appended last (stable int value). Concierge: no partner could serve the request.
    Unmatched,
}
