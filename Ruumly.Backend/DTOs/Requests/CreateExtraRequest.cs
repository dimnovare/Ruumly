namespace Ruumly.Backend.DTOs.Requests;

public record CreateExtraRequest(
    string Key,
    string Label,
    string? Description,
    decimal SupplierPrice,
    int? SortOrder);

public record UpdateExtraRequest(
    string? Label,
    string? Description,
    decimal? SupplierPrice,
    bool? IsActive,
    int? SortOrder);
