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
    /// <summary>data:image/png;base64,... from canvas (required for canvas path; ignored for eID path).</summary>
    string  SignatureDataUrl,
    /// <summary>"smartid" | "mobileid" — when set, eID path is used instead of canvas.</summary>
    string? SigningMethod = null,
    /// <summary>Session id from POST /identity/start — required when SigningMethod is "smartid" or "mobileid".</summary>
    string? VerifiedSessionId = null
);

public record PreviewContractRequest(
    Guid BookingId,
    Guid ContractTemplateId
);

public record StartIdentityVerificationRequest(
    Guid   BookingId,
    /// <summary>"smartid" or "mobileid"</summary>
    [MaxLength(20)] string Method,
    /// <summary>National personal identification code, e.g. "38001085718".</summary>
    [MaxLength(30)]  string PersonalCode,
    /// <summary>Two-letter country code: "EE", "LV", or "LT". Defaults to "EE" when omitted.</summary>
    [MaxLength(2)]   string? Country = null,
    /// <summary>Phone number in international format — required for Mobile-ID.</summary>
    [MaxLength(20)]  string? PhoneNumber = null
);

public record InitiateDokobitSigningRequest(
    Guid   BookingId,
    Guid   ContractTemplateId,
    [MaxLength(200)] string SignerName,
    [MaxLength(20)]  string SignerIdCode,
    [MaxLength(200)] string SignerEmail
);
