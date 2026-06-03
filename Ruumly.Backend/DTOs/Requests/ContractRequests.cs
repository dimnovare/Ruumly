using System.ComponentModel.DataAnnotations;

namespace Ruumly.Backend.DTOs.Requests;

public record CreateContractTemplateRequest(
    [MaxLength(200)]     string  Name,
    [MaxLength(500_000)] string  Html,   // ~500 KB — generous for real contracts
    bool?  IsDefault
);

public record UpdateContractTemplateRequest(
    [MaxLength(200)]     string? Name,
    [MaxLength(500_000)] string? Html,
    bool?   IsActive,
    bool?   IsDefault
);

public record SignContractRequest(
    Guid    BookingId,
    Guid    ContractTemplateId,
    string  TenantName,
    string? TenantIdCode,
    /// <summary>data:image/png;base64,... from canvas</summary>
    string  SignatureDataUrl
);

public record PreviewContractRequest(
    Guid BookingId,
    Guid ContractTemplateId
);

public record InitiateDokobitSigningRequest(
    Guid   BookingId,
    Guid   ContractTemplateId,
    [MaxLength(200)] string SignerName,
    [MaxLength(20)]  string SignerIdCode,
    [MaxLength(200)] string SignerEmail
);
