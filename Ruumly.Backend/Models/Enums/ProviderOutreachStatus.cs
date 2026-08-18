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

    /// <summary>
    /// The provider answered, but cannot price the request yet — they told us
    /// what is missing through the quote page (see ProviderInfoRequest).
    ///
    /// Appended rather than folded into an existing member because none of them
    /// is true:
    ///   • Sent/NoAnswer say the provider is SILENT. This one replied — that is
    ///     the entire point, and reporting it as silence is what let a real
    ///     answer sit unnoticed in a shared inbox.
    ///   • Declined says they will not take the job. They have not said that;
    ///     they asked a question, and most of these convert once answered.
    ///   • Replied is the closest, and is the one that would have quietly done
    ///     damage: the concierge match-rate metric counts a Replied outreach as
    ///     a supplier MATCH (AdminLeadsController.GetLeadMetrics). A provider
    ///     who cannot quote is precisely not a match yet, so reusing Replied
    ///     would inflate the north-star number with the requests we are stuck on.
    /// Last, because the column persists enum NAMES — never reorder or rename.
    /// </summary>
    NeedsInfo,
}
