namespace Ruumly.Backend.Models;

public class ListingExtra
{
    public Guid Id { get; set; }
    public Guid ListingId { get; set; }
    public Listing Listing { get; set; } = null!;

    /// <summary>Machine key used in booking extras array (e.g. "packing", "climate-control")</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Display label shown to customer (e.g. "Pakkimisabi")</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Optional description</summary>
    public string? Description { get; set; }

    /// <summary>Supplier's public price for this service (what they charge walk-in customers)</summary>
    public decimal PublicPrice { get; set; }

    /// <summary>
    /// Negotiated partner discount % on this specific extra.
    /// Null = use supplier's base PartnerDiscountRate.
    /// </summary>
    public decimal? PartnerDiscountRate { get; set; }

    /// <summary>
    /// What Ruumly pays the supplier.
    /// Auto-calculated: PublicPrice × (1 - effectivePartnerDiscount/100)
    /// </summary>
    public decimal SupplierPrice { get; set; }

    /// <summary>
    /// What the customer pays. Auto-calculated from partner discount
    /// minus ruumlyMinMargin, unless CustomerPriceOverride is set.
    /// </summary>
    public decimal CustomerPrice { get; set; }

    /// <summary>
    /// If set, this value is used as CustomerPrice instead of auto-calculation.
    /// Allows admin to manually control the customer-facing price per extra.
    /// Null = auto-calculate using formula.
    /// </summary>
    public decimal? CustomerPriceOverride { get; set; }

    /// <summary>Whether this extra is currently available for booking</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Display order in the booking form</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
