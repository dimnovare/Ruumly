namespace Ruumly.Backend.DTOs.Responses;

public record SupplierLocationDto(
    Guid          Id,
    Guid          SupplierId,
    string        SupplierName,
    string        Name,
    string        Address,
    string        City,
    double        Lat,
    double        Lng,
    string?       Notes,
    List<string>  Images,
    string        Description,
    string?       OpeningHours,
    bool              IsActive,
    int               UnitCount,
    int               AvailableUnits,
    bool              FullyBooked,
    decimal?          PriceFrom,
    string            CreatedAt,
    List<ListingDto>  Units,
    decimal           Rating,
    int               ReviewCount,
    decimal?          BestCustomerDiscount,
    string?           ExternalId,
    /// <summary>True for unclaimed directory pins: supplier is a directory profile and this site has zero units.</summary>
    bool              IsDirectory = false,
    /// <summary>Supplier's partner-page slug, when one is set — lets the map pin link to /partner/{slug}.</summary>
    string?           SupplierSlug = null,
    /// <summary>Parsed Supplier.ServiceTypesJson category slugs; empty when none.</summary>
    List<string>?     ServiceTypes = null,
    /// <summary>Supplier logo, when set — lets search cards / map popups show the company mark.</summary>
    string?           SupplierLogoUrl = null
);
