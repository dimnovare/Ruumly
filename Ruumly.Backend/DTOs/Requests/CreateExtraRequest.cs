using System.ComponentModel.DataAnnotations;

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
    [Range(0, 100)]             decimal? PartnerDiscountRate,
    [Range(0, double.MaxValue)] decimal? CustomerPriceOverride,
    bool? IsActive,
    int? SortOrder);
