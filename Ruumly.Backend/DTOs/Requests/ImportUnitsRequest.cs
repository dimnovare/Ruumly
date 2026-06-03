namespace Ruumly.Backend.DTOs.Requests;

public record ImportUnitsRequest(List<ImportUnitRow> Units);

public record ImportUnitRow(
    string? Title,
    string? Type,
    decimal PriceFrom,
    string? PriceUnit,
    decimal? SizeM2,
    int? QuantityTotal,
    string? Description,
    decimal? VatRate,
    Dictionary<string, object>? Features = null);

/// <summary>Request body for POST /api/admin/locations/bulk — create a location + its units in one call.</summary>
public record BulkCreateLocationRequest(
    Guid?         SupplierId,
    string        Name,
    string        Address,
    string        City,
    double        Lat,
    double        Lng,
    List<ImportUnitRow>? Units);
