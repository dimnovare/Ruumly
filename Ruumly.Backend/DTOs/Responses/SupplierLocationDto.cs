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
    string?           ExternalId
);
