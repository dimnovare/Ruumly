using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.DTOs.Requests;

/// <summary>
/// Creates a new listing unit within a specific location.
/// </summary>
public record CreateUnitRequest(
    string      Title,
    ListingType Type,
    decimal     PriceFrom,
    string      PriceUnit,
    decimal?    SizeM2,
    int?        QuantityTotal,
    string?     Description,
    decimal?    VatRate,
    bool        PricesIncludeVat
);
