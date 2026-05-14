namespace Ruumly.Backend.DTOs.Responses;

public record ContractTemplateDto(
    Guid   Id,
    string Name,
    string HtmlTemplate,
    bool   IsActive,
    bool   IsDefault,
    string CreatedAt,
    string UpdatedAt
);
