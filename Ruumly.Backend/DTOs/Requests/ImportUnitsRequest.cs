namespace Ruumly.Backend.DTOs.Requests;

public record ImportUnitsRequest(List<ImportUnitRow> Units);

public record ImportUnitRow(
    string Title,
    string Type,
    decimal PriceFrom,
    string? PriceUnit,
    decimal? SizeM2,
    int? Quantity,
    string? Description);
