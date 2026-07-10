namespace Ruumly.Backend.Models.Enums;

// Stored as enum NAMES (string conversion in RuumlyDbContext) — append-only,
// never reorder or rename persisted members.
public enum OfferStatus
{
    Draft,
    Sent,
    Viewed,
    Chosen,
    Expired,
}
