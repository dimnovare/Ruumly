namespace Ruumly.Backend.DTOs.Requests;

public record UpdateSupplierProfileRequest(
    string? Name,
    string? RegistryCode,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone);
