using System.ComponentModel.DataAnnotations;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

/// <summary>
/// One availability-request email sent to a provider for a
/// <see cref="DemandLead"/> ("a customer near {city} needs {category} — can
/// you take it?"). The customer's identity is never shared — the admin
/// brokers the introduction. Status is updated manually from the provider's
/// reply; <see cref="SentTo"/> snapshots the address so history survives
/// later contact-email changes.
/// </summary>
public class ProviderOutreach
{
    public Guid Id { get; set; }
    public Guid DemandLeadId { get; set; }
    public DemandLead? DemandLead { get; set; }
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>Email snapshot at send time.</summary>
    [MaxLength(200)] public string SentTo { get; set; } = string.Empty;

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public ProviderOutreachStatus Status { get; set; } = ProviderOutreachStatus.Sent;

    // ── Did the message actually get there? (2026-08-18) ──────────────────────
    //
    // SentAt only records that we handed the mail to Resend. On 2026-08-18 the
    // first real read of the metrics found 18 provider contacts across five
    // Viljandi storage requests -- three of the providers based in Viljandi
    // itself -- with every row still Sent: no quote, no decline, no bounce.
    //
    // That single fact has three completely different explanations, each needing
    // a different fix, and nothing recorded could tell them apart:
    //   1. the mail never reached an inbox (spam placement, domain reputation);
    //   2. it arrived and was never opened (subject line, unrecognised sender);
    //   3. it was read and ignored (the ask itself does not appeal).
    // The webhook subscribed to bounced and complained only, so absence of a
    // bounce was the entire signal.
    //
    // These two turn one opaque quote rate into a funnel -- sent, delivered,
    // opened, quoted -- which is also the only way to judge whether the
    // quote-page work shipped the same day changed anything.
    //
    // Nullable and never backfilled: rows sent before this existed genuinely do
    // not know, and a zero would read as "not delivered" rather than "unknown".

    /// <summary>When Resend confirmed delivery to the receiving server.</summary>
    public DateTime? DeliveredAt { get; set; }

    /// <summary>
    /// First open reported by Resend. Advisory only: opens are measured with a
    /// tracking pixel that many clients block, so a null here is weak evidence
    /// of not-read, while a non-null is strong evidence of read. Never treat the
    /// absence as a fact about the provider.
    /// </summary>
    public DateTime? OpenedAt { get; set; }
    [MaxLength(2000)] public string? Note { get; set; }

    // ── Tokenized provider quote (2026-07-16) ─────────────────────────────────
    // Each SENT row gets its own 256-bit url-safe token so the provider can open
    // /{lang}/quote/{token} and submit a price without an account. Legacy rows
    // (sent before this feature) keep a null token — no link, no backfill. The
    // provider's answer lands here (Quoted*), flips Status to Replied, and
    // auto-seeds an OfferOption on the lead's draft offer (see QuoteController).
    /// <summary>256-bit url-safe base64 token (43 chars) — the only credential for the public quote page.</summary>
    [MaxLength(64)] public string? QuoteToken { get; set; }
    public decimal? QuotedAmount { get; set; }
    [MaxLength(40)]   public string? QuotedUnit { get; set; }
    [MaxLength(200)]  public string? QuotedAvailability { get; set; }
    [MaxLength(2000)] public string? QuotedNote { get; set; }
    public DateTime? QuotedAt { get; set; }
}
