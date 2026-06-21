namespace Ruumly.Backend.Models.Enums;

public enum DemandLeadStatus
{
    New,
    Contacted,
    Converted,
    Dismissed,
    // Appended last (stable int value). The partner has sent the customer a price.
    Quoted,
}
