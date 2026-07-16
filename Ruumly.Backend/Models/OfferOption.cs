using System.ComponentModel.DataAnnotations;

namespace Ruumly.Backend.Models;

/// <summary>
/// One selectable option inside an <see cref="Offer"/>. Supplier linkage is
/// optional so the admin can add free-form options (a provider not yet in the
/// system, a manual quote, etc.). PATCH uses replace-set semantics: options
/// are always rewritten as a whole set, never edited row by row.
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
}
