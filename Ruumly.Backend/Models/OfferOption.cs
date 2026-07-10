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
}
