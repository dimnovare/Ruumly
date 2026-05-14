namespace Ruumly.Backend.DTOs.Responses;

public record ContractTemplateDto(
    Guid   Id,
    string Name,
    string Html,
    bool   IsActive,
    bool   IsDefault,
    string CreatedAt,
    string UpdatedAt
);
