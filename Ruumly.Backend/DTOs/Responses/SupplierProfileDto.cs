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
    bool    HasGoogleReviews);

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
