using System.ComponentModel.DataAnnotations;

namespace Ruumly.Backend.Models;

/// <summary>
/// One delivery-failure webhook from Resend (email.bounced / email.complained),
/// recorded verbatim before it is applied.
///
/// This table is the idempotency guard for a public, unauthenticated endpoint
/// that the sender RETRIES: <see cref="EventId"/> is the Svix message id
/// (svix-id header) and carries a unique index, so a redelivery of the same
/// event loses the insert race and is skipped instead of re-marking rows. It
/// doubles as the audit trail — "why is this supplier flagged unreachable" is
/// answerable from stored data, not from Resend's dashboard.
/// </summary>
public class EmailDeliveryEvent
{
    public Guid Id { get; set; }

    /// <summary>Svix message id (svix-id / webhook-id header) — globally unique per event.</summary>
    [MaxLength(120)]
    public string EventId { get; set; } = string.Empty;

    /// <summary>Resend event name, e.g. "email.bounced" | "email.complained".</summary>
    [MaxLength(60)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>The affected address, lower-cased for matching.</summary>
    [MaxLength(200)]
    public string Recipient { get; set; } = string.Empty;

    /// <summary>"hard" | "soft" | "complaint" — normalized from Resend's bounce.type.</summary>
    [MaxLength(20)]
    public string? BounceType { get; set; }

    /// <summary>Resend's bounce.subType (e.g. "Suppressed", "MailboxFull").</summary>
    [MaxLength(60)]
    public string? BounceSubType { get; set; }

    /// <summary>Human-readable reason, verbatim from the provider (truncated).</summary>
    [MaxLength(500)]
    public string? Reason { get; set; }

    /// <summary>The supplier matched by address — null when nobody owns it (e.g. a customer email).</summary>
    public Guid? SupplierId { get; set; }

    /// <summary>How many ProviderOutreach rows this event flipped away from Sent.</summary>
    public int OutreachRowsUpdated { get; set; }

    /// <summary>Resend's created_at for the event, when parseable.</summary>
    public DateTime? OccurredAt { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}
