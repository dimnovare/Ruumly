using System.ComponentModel.DataAnnotations;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

/// <summary>
/// A concierge offer: the admin's curated set of options for a
/// <see cref="DemandLead"/>, published to the customer on an unguessable
/// tokenized page (https://ruumly.eu/{lang}/offer/{token}). Lifecycle:
/// Draft → Sent (email out, lead auto-Quoted) → Viewed (first public GET)
/// → Chosen (customer picked an option) — or Expired (manually closed).
/// </summary>
public class Offer
{
    public Guid Id { get; set; }
    public Guid DemandLeadId { get; set; }
    public DemandLead? DemandLead { get; set; }

    /// <summary>
    /// 32 bytes of crypto randomness, url-safe base64 without padding
    /// (43 chars). The only public credential for the offer page.
    /// </summary>
    [MaxLength(64)] public string Token { get; set; } = string.Empty;

    public OfferStatus Status { get; set; } = OfferStatus.Draft;
    public string Language { get; set; } = "et";

    /// <summary>Optional admin-written note shown to the customer on the offer page.</summary>
    [MaxLength(2000)] public string? CustomerNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public DateTime? ViewedAt { get; set; }
    public DateTime? ChosenAt { get; set; }

    /// <summary>Plain column (no FK) — points at one of this offer's options.</summary>
    public Guid? ChosenOptionId { get; set; }

    /// <summary>Admin email (falls back to user id when the claim is absent).</summary>
    [MaxLength(200)] public string? CreatedBy { get; set; }

    public List<OfferOption> Options { get; set; } = [];
}
