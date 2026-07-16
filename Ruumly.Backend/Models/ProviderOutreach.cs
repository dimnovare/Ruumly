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
