namespace Ruumly.Backend.DTOs.Responses;

public record PaidFeatureDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string Category,
    string Scope,
    decimal PriceAmount,
    string PriceCurrency,
    string BillingInterval,
    bool IsActive,
    int SortOrder);

public record SupplierPaidFeatureDto(
    Guid Id,
    Guid SupplierId,
    PaidFeatureDto PaidFeature,
    Guid? ListingId,
    Guid? LocationId,
    bool IsActive,
    DateTime StartsAt,
    DateTime? EndsAt,
    string? AdminNotes);

public record PaidFeatureRequestDto(
    Guid Id,
    Guid SupplierId,
    string? SupplierName,
    PaidFeatureDto PaidFeature,
    Guid? ListingId,
    Guid? LocationId,
    Guid? RequestedByUserId,
    Guid? ReviewedByUserId,
    string Status,
    string? Message,
    string? AdminNotes,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record ProviderPaidFeaturesDto(
    List<PaidFeatureDto> Catalog,
    List<SupplierPaidFeatureDto> ActiveFeatures,
    List<PaidFeatureRequestDto> Requests);
