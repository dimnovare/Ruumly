namespace Ruumly.Backend.DTOs.Requests;

public record CreateLocationRequest(
    Guid    SupplierId,
    string  Name,
    string  Address,
    string  City,
    double  Lat,
    double  Lng,
    string? Notes
);

public record PatchLocationRequest(
    string? Name,
    string? Address,
    string? City,
    double? Lat,
    double? Lng,
    string? Notes
);
