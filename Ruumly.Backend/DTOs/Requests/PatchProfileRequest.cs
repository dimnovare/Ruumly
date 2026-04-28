namespace Ruumly.Backend.DTOs.Requests;

public record PatchProfileRequest(
    string? Name,
    string? Phone,
    string? Company
);
