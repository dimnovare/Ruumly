using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.DTOs.Requests;

/// <summary>
/// Creates a new listing within a specific supplier location.
/// </summary>
public record CreateLocationListingRequest(
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
