namespace Ruumly.Backend.DTOs.Requests;

public record CreateLocationRequest(
    Guid          SupplierId,
    string        Name,
    string        Address,
    string        City,
    double        Lat,
    double        Lng,
    string?       Notes,
    List<string>? Images,
    string?       Description,
    string?       OpeningHours
);

public record PatchLocationRequest(
    string?       Name,
    string?       Address,
    string?       City,
    double?       Lat,
    double?       Lng,
    string?       Notes,
    List<string>? Images,
    string?       Description,
    string?       OpeningHours
);
