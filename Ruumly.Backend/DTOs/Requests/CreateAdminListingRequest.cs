namespace Ruumly.Backend.DTOs.Requests;

public record CreateAdminListingRequest(
    string              Type,
    string              Title,
    Guid                SupplierId,
    string              Address,
    string              City,
    double?             Lat,
    double?             Lng,
    decimal             PriceFrom,
    string              PriceUnit,
    bool                AvailableNow,
    string?             Badge,
    string              Description,
    List<string>?       Images,
    Dictionary<string, object>? Features,
    decimal?            PartnerDiscountRateOverride,
    decimal?            ClientDiscountRateOverride,
    decimal?            VatRate,
    bool                PricesIncludeVat,
    Guid?               LocationId,
    int?                QuantityTotal,
    decimal?            SizeM2
);

public record UpdateImagesRequest(List<string> Images);

/// <summary>One row of a bulk listing import (minimal fields; the rest default).</summary>
public record BulkListingRow(
    string   Title,
    string   Type,
    string   City,
    string?  Address,
    decimal  PriceFrom,
    string?  PriceUnit,
    decimal? SizeM2,
    int?     QuantityTotal,
    string?  Description
);

public record BulkCreateListingsRequest(Guid SupplierId, List<BulkListingRow> Listings);

public record BulkRowResult(int Index, string Title, bool Ok, Guid? ListingId, string? Error);

public record PatchAdminListingRequest(
    string?             Type,
    string?             Title,
    Guid?               SupplierId,
    string?             Address,
    string?             City,
    double?             Lat,
    double?             Lng,
    decimal?            PriceFrom,
    string?             PriceUnit,
    bool?               AvailableNow,
    bool?               IsActive,
    string?             Badge,
    string?             Description,
    List<string>?       Images,
    Dictionary<string, object>? Features,
    decimal?            PartnerDiscountRateOverride,
    decimal?            ClientDiscountRateOverride,
    decimal?            VatRate,
    bool?               PricesIncludeVat,
    Guid?               LocationId,
    int?                QuantityTotal,
    decimal?            SizeM2,
    Dictionary<string, string>? DescriptionTranslations = null
);
