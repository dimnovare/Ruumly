namespace Ruumly.Backend.Models.Enums;

// What the dispute is about. Stored as a string (HasConversion) so values are
// stable and human-readable in the DB.
public enum DisputeType
{
    Damage,   // goods/property damaged during the service
    NoShow,   // partner or customer did not show up
    Deposit,  // trailer/security-deposit return disagreement
    Other,
}

// Admin resolution workflow. Open → InReview → Resolved | Rejected (both terminal).
public enum DisputeStatus
{
    Open,
    InReview,
    Resolved,
    Rejected,
}
