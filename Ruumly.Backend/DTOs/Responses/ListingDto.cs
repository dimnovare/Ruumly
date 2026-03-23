namespace Ruumly.Backend.DTOs.Responses;

public record ListingDto(
    Guid             Id,
    string           Type,          // "warehouse" | "moving" | "trailer"
    string           Title,
    string           SupplierName,
    string           Address,
    string           City,
    double           Lat,
    double           Lng,
    decimal          PriceFrom,
    string           PriceUnit,
    bool             AvailableNow,
    string?          Badge,         // "cheapest" | "closest" | "best-value" | "promoted" | null
    decimal          Rating,
    int              ReviewCount,
    string           Description,
    List<string>     Images,
    Dictionary<string, object> Features,
    decimal?         PartnerDiscountRateOverride,
    decimal?         ClientDiscountRateOverride,
    decimal?         VatRate,
    bool             PricesIncludeVat
);
