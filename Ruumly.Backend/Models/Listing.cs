using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using NpgsqlTypes;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

public class Listing
{
    public Guid Id { get; set; }
    public ListingType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    // For location-attached listings (LocationId != null), these fields are
    // snapshots taken at creation time and MUST NOT be read directly — they
    // drift from the parent SupplierLocation. Read via Location?.X ?? X.
    // Kept only for standalone listings (LocationId == null), which have no parent.
    // Future refactor: migrate all standalone listings to auto-generated
    // SupplierLocation rows, then drop these four columns entirely.
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lng { get; set; }
    public decimal PriceFrom { get; set; }
    public string PriceUnit { get; set; } = string.Empty;

    // TODO: Add MinBookingDays / MinBookingMonths field for suppliers who
    // require a minimum commitment (common in storage). UI should display
    // "1 month minimum" as a badge on the listing card and enforce server-side.
    // Out of scope until v2.
    public bool AvailableNow { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public string Description { get; set; } = string.Empty;
    // Default-language fallback used by Featured cards, search snippets, and
    // when a per-language translation isn't present.

    public string DescriptionTranslationsJson { get; set; } = "{}";
    // JSON: { "et": "...", "en": "...", "ru": "...", "lv": "...", "lt": "..." }
    // Backend reads/writes via DescriptionTranslations dict.

    [NotMapped]
    public Dictionary<string, string> DescriptionTranslations
    {
        get => JsonSerializer.Deserialize<Dictionary<string, string>>(DescriptionTranslationsJson) ?? new();
        set => DescriptionTranslationsJson = JsonSerializer.Serialize(value);
    }

    public ListingBadge? Badge { get; set; }
    public decimal Rating { get; set; }
    public int ReviewCount { get; set; }
    public int ViewCount { get; set; } = 0;
    public decimal? PartnerDiscountRateOverride { get; set; }
    public decimal? ClientDiscountRateOverride { get; set; }
    public decimal? VatRate { get; set; }
    public bool PricesIncludeVat { get; set; } = true;

    // Per-vertical commercial fields:
    //  - DepositAmount: refundable security deposit (trailer especially; storage sometimes).
    //  - RequiresLicenseCategory: driving-licence class a trailer renter must hold (e.g. "B" / "BE").
    //  - MinBookingMonths: minimum committed rental period for storage (guards sub-period bookings).
    public decimal? DepositAmount { get; set; }
    public string? RequiresLicenseCategory { get; set; }
    public int? MinBookingMonths { get; set; }

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

    public Guid? LocationId { get; set; }
    public SupplierLocation? Location { get; set; }
    public int? QuantityTotal { get; set; }
    public decimal? SizeM2 { get; set; }

    /// <summary>
    /// Maintained by the trg_listings_search trigger.
    /// Never written by EF — always DB-generated.
    /// </summary>
    public NpgsqlTsVector? SearchVector { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<Booking> Bookings { get; set; } = [];
    public List<ListingExtra> Extras { get; set; } = [];
}
