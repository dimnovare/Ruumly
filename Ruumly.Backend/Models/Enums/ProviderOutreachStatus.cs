namespace Ruumly.Backend.Models.Enums;

// Stored as enum NAMES (string conversion in RuumlyDbContext) — append-only,
// never reorder or rename persisted members. Updated manually by the admin
// from provider replies (no webhook/link tracking this round).
public enum ProviderOutreachStatus
{
    Sent,
    Replied,
    Declined,
    NoAnswer,
}
