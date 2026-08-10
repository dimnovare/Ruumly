namespace Ruumly.Backend.Models.Enums;

// Stored as enum NAMES (string conversion in RuumlyDbContext) — append-only,
// never reorder or rename persisted members. Sent/Replied/Declined/NoAnswer are
// set manually by the admin from provider replies; Bounced/Complained are set
// ONLY by the Resend webhook (Controllers/ResendWebhookController) — they mean
// the email never reached a human, which is a different fact from silence.
public enum ProviderOutreachStatus
{
    Sent,
    Replied,
    Declined,
    NoAnswer,

    /// <summary>The address rejected the message (Resend email.bounced).</summary>
    Bounced,

    /// <summary>The recipient marked it as spam (Resend email.complained).</summary>
    Complained,
}
