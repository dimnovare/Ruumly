namespace Ruumly.Backend.Models;

public class SupplierLocation
{
    public Guid Id { get; set; }
    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = "EE";
    public double Lat { get; set; }
    public double Lng { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string ImagesJson { get; set; } = "[]";

    [System.Text.Json.Serialization.JsonIgnore]
    public List<string> Images
    {
        get => System.Text.Json.JsonSerializer.Deserialize<List<string>>(ImagesJson) ?? [];
        set => ImagesJson = System.Text.Json.JsonSerializer.Serialize(value);
    }

    public string Description { get; set; } = string.Empty;
    public string? OpeningHours { get; set; }

    /// <summary>
    /// True when this Location was auto-created as a data-grouping shell.
    /// Synthetic Locations have no user-curated content (no images, no description)
    /// and are hidden from public-facing browse surfaces. Their listings appear
    /// directly in /api/listings search results instead of being shown via the
    /// Location card (which would have nothing to display).
    /// </summary>
    public bool IsSynthetic { get; set; } = false;

    /// <summary>Total physical units at this site (e.g. 27 in "2/27 available").</summary>
    public int? TotalUnitCount { get; set; }

    /// <summary>Currently available units at this site (e.g. 2 in "2/27 available").</summary>
    public int? AvailableUnitCount { get; set; }

    public List<Listing> Listings { get; set; } = [];
}
