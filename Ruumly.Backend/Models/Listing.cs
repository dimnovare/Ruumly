using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

public class Listing
{
    public Guid Id { get; set; }
    public ListingType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lng { get; set; }
    public decimal PriceFrom { get; set; }
    public string PriceUnit { get; set; } = string.Empty;
    public bool AvailableNow { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public string Description { get; set; } = string.Empty;
    public ListingBadge? Badge { get; set; }
    public decimal Rating { get; set; }
    public int ReviewCount { get; set; }
    public decimal? PartnerDiscountRateOverride { get; set; }
    public decimal? ClientDiscountRateOverride { get; set; }
    public decimal? VatRate { get; set; }
    public bool PricesIncludeVat { get; set; } = false;

    /// <summary>
    /// JSON array of image URLs. First element is the cover image.
    /// Exposes as Images; Images[0] maps to the frontend `image` field.
    /// </summary>
    public string ImagesJson { get; set; } = "[]";

    [NotMapped]
    public List<string> Images
    {
        get => JsonSerializer.Deserialize<List<string>>(ImagesJson) ?? [];
        set => ImagesJson = JsonSerializer.Serialize(value);
    }

    [NotMapped]
    public string Image => Images.FirstOrDefault() ?? string.Empty;

    /// <summary>
    /// JSON object holding all type-specific feature flags and properties:
    /// Warehouse: heated, indoor, access24_7, security, loadingDock, forklift, shortTerm, longTerm, size, sizeUnit, features[]
    /// Moving:    withVan, packingHelp, loadingHelp, pricingModel, serviceArea[], services[]
    /// Trailer:   trailerType, weightClass, requirements[]
    /// </summary>
    public string FeaturesJson { get; set; } = "{}";

    [NotMapped]
    public Dictionary<string, object> Features
    {
        get => JsonSerializer.Deserialize<Dictionary<string, object>>(FeaturesJson) ?? [];
        set => FeaturesJson = JsonSerializer.Serialize(value);
    }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<Booking> Bookings { get; set; } = [];
}
