namespace Ruumly.Backend.DTOs.Requests;

public record UpdateSupplierProfileRequest(
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone);
