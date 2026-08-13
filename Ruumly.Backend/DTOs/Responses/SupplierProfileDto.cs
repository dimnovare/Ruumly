namespace Ruumly.Backend.DTOs.Responses;

public record SupplierProfileDto(
    Guid    Id,
    string  Slug,
    string  Name,
    string  Country,
    string? Tagline,
    Dictionary<string, string>? LongDescription,   // parsed from LongDescriptionTranslationsJson
    string? LogoUrl,
    string? HeroImageUrl,
    string? WebsiteUrl,
    int?    FoundedYear,
    decimal Rating,
    int     ReviewCount,
    string  Tier,
    bool    IsVerified,
    bool    FoundingPartner,
    int     LocationCount,
    int     ListingCount,
    List<SupplierProfileLocationDto> Locations,
    /// <summary>True when a Google Place ID is configured — lets the frontend decide whether to render the reviews section.</summary>
    bool    HasGoogleReviews,
    /// <summary>True for unclaimed directory profiles (imported, no owner, no commerce).</summary>
    bool    IsDirectory = false,
    /// <summary>Service category slugs from Supplier.ServiceTypesJson; null/empty when none.</summary>
    List<string>? ServiceTypes = null,
    /// <summary>
    /// The provider's own headline "from" price and unit, set through the claim
    /// form. Null until they give us one — the page shows nothing rather than a
    /// zero or a "price on request" placeholder, because an empty space is
    /// honest and a fabricated placeholder is not.
    /// </summary>
    decimal? PriceFrom = null,
    string? PriceUnit = null,
    string? PriceNote = null);

public record SupplierProfileLocationDto(
    Guid     Id,
    string   Name,
    string   Address,
    string   City,
    string   Country,
    double   Lat,
    double   Lng,
    string?  OpeningHours,
    string?  Description,
    List<string> Images,
    int      ListingCount,
    int?     TotalUnitCount,
    int?     AvailableUnitCount);
