namespace Ruumly.Backend.DTOs.Responses;

public record SupplierLocationDto(
    Guid    Id,
    Guid    SupplierId,
    string  Name,
    string  Address,
    string  City,
    double  Lat,
    double  Lng,
    string? Notes,
    string  CreatedAt
);
