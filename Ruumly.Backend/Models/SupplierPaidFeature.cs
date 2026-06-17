namespace Ruumly.Backend.Models;

public class SupplierPaidFeature
{
    public Guid Id { get; set; }

    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public Guid PaidFeatureId { get; set; }
    public PaidFeature PaidFeature { get; set; } = null!;

    public Guid? ListingId { get; set; }
    public Listing? Listing { get; set; }

    public Guid? LocationId { get; set; }
    public SupplierLocation? Location { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime StartsAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndsAt { get; set; }
    public string? AdminNotes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
