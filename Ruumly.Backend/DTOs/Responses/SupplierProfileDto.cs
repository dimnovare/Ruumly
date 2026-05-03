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
    List<SupplierProfileLocationDto> Locations);

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
