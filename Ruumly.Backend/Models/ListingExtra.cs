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

    /// <summary>Supplier's price for this extra service (what they charge)</summary>
    public decimal SupplierPrice { get; set; }

    /// <summary>Price shown to customer (supplierPrice + Ruumly margin)</summary>
    public decimal CustomerPrice { get; set; }

    /// <summary>Whether this extra is currently available for booking</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Display order in the booking form</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
