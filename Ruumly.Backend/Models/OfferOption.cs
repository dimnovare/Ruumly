using System.ComponentModel.DataAnnotations;

namespace Ruumly.Backend.Models;

/// <summary>
/// One selectable option inside an <see cref="Offer"/>. Supplier linkage is
/// optional so the admin can add free-form options (a provider not yet in the
/// system, a manual quote, etc.). PATCH sends the whole set at once, but the
/// rows are matched on Id and updated in place — an admin's edit must not cost
/// an option its identity, because provenance lives on the row.
/// </summary>
public class OfferOption
{
    public Guid Id { get; set; }
    public Guid OfferId { get; set; }
    public Offer? Offer { get; set; }

    public Guid? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public Guid? SupplierLocationId { get; set; }
    public SupplierLocation? SupplierLocation { get; set; }

    [MaxLength(200)]  public string Title { get; set; } = string.Empty;
    public decimal? PriceAmount { get; set; }
    [MaxLength(40)]   public string? PriceUnit { get; set; }
    [MaxLength(2000)] public string? Notes { get; set; }
    public int SortOrder { get; set; }

    /// <summary>
    /// The <see cref="ProviderOutreach"/> whose tokenized quote seeded this
    /// option; null for admin-authored options. Plain column (no FK) — the
    /// option must outlive its outreach row, mirroring Offer.ChosenOptionId.
    ///
    /// It is both the "from provider quote" marker AND the key the quote's
    /// add-or-update matches on: a re-submit only ever touches the option IT
    /// created, so a provider can never overwrite (or silently re-title) an
    /// admin-authored option for the same supplier.
    /// </summary>
    public Guid? CreatedFromOutreachId { get; set; }

    // ── Provider outcome notifications (2026-08-20) ───────────────────────────
    //
    // EXACTLY-ONCE MARKERS, and they live HERE rather than on Offer or on
    // ProviderOutreach for three reasons:
    //
    //   • The option IS the per-provider-per-offer row, which is exactly the
    //     grain these notifications have. Offer is too coarse (one offer, N
    //     providers) and ProviderOutreach is not present for an admin-authored
    //     option that carries only a SupplierId.
    //   • POST /admin/offers/{id}/send is REPEATABLE — it refreshes SentAt and
    //     bumps Version every time, and an admin who fixes a price and re-sends
    //     is doing something ordinary. Without a marker each re-send would tell
    //     every provider again that their quote had just gone out.
    //   • A row-level timestamp survives the option being edited in place: PATCH
    //     matches on Id and updates, so the marker is not lost the way a
    //     derived-from-status guess would be.
    //
    // Nullable and never backfilled: options that predate this feature genuinely
    // were never notified, and stamping them "now" would be a lie that suppresses
    // the first real notification they are due.

    /// <summary>
    /// When this option's provider was told their quote had been forwarded to
    /// the customer. Null = not yet told.
    /// </summary>
    public DateTime? ProviderNotifiedSentAt { get; set; }

    /// <summary>
    /// When this option's provider was told the outcome (won or lost). Null =
    /// not yet told. One column for both verdicts because a provider receives
    /// exactly one of them, and which one is derivable from
    /// <see cref="Offer.ChosenOptionId"/>.
    /// </summary>
    public DateTime? ProviderNotifiedOutcomeAt { get; set; }
}
