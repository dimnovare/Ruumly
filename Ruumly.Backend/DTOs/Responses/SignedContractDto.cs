namespace Ruumly.Backend.DTOs.Responses;

public record SignedContractDto(
    Guid    Id,
    Guid    BookingId,
    string  RenderedHtml,
    string  TenantName,
    string? TenantIdCode,
    string  TenantEmail,
    string  SignedAt
    // SignatureDataUrl is intentionally OMITTED (stored for audit, not exposed to API consumers)
);
