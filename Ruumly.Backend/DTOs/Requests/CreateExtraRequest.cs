namespace Ruumly.Backend.DTOs.Requests;

public record CreateExtraRequest(
    string Key,
    string Label,
    string? Description,
    decimal PublicPrice,
    decimal? PartnerDiscountRate,
    decimal? CustomerPriceOverride,
    int? SortOrder);

public record UpdateExtraRequest(
    string? Label,
    string? Description,
    decimal? PublicPrice,
    decimal? PartnerDiscountRate,
    decimal? CustomerPriceOverride,
    bool? IsActive,
    int? SortOrder);
