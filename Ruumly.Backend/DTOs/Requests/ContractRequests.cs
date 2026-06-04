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
    /// <summary>Optional override — when blank, derived from the booking (ContactName ?? User.Name).</summary>
    [MaxLength(200)] string? SignerName = null,
    /// <summary>Optional override — passed through as-is; the VERIFIED code is captured post-sign.</summary>
    [MaxLength(20)]  string? SignerIdCode = null,
    /// <summary>Optional override — when blank, derived from the booking (ContactEmail ?? User.Email).</summary>
    [MaxLength(200)] string? SignerEmail = null,
    /// <summary>Optional — pin a specific template. When null, the supplier's active docx template is used.</summary>
    Guid?  ContractTemplateId = null,
    /// <summary>ISO alpha-2 country for the eID provider; defaults to the booking supplier's country, else "EE".</summary>
    [MaxLength(2)] string? SignerCountryCode = null,
    /// <summary>Optional phone for Mobile-ID prefill.</summary>
    [MaxLength(20)] string? SignerPhone = null
);
