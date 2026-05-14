namespace Ruumly.Backend.DTOs.Requests;

public record CreateContractTemplateRequest(
    string Name,
    string HtmlTemplate,
    bool?  IsDefault
);

public record UpdateContractTemplateRequest(
    string? Name,
    string? HtmlTemplate,
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
