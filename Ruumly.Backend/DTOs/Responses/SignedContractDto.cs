namespace Ruumly.Backend.DTOs.Responses;

public record SignedContractDto(
    Guid    Id,
    Guid    BookingId,
    string  RenderedHtml,
    string  TenantName,
    string? TenantIdCode,
    string  TenantEmail,
    string  SignedAt,
    string  SigningMethod,   // "canvas" | "dokobit" | "smartid" | "mobileid"
    bool    Verified         // true only for eID paths (identity-verified); false for self-declared canvas
    // SignatureDataUrl is intentionally OMITTED (stored for audit, not exposed to API consumers)
);
