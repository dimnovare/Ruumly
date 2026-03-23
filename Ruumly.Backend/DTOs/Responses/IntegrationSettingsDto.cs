namespace Ruumly.Backend.DTOs.Responses;

public record IntegrationSettingsDto(
    Guid    Id,
    Guid    SupplierId,
    string  SupplierName,
    string  ApprovalMode,
    string  PostingMode,
    string  FallbackPostingMode,
    string? MappingProfile,
    string? LastTestedAt,
    string? LastTestResult,
    bool    IsActive,
    string  UpdatedAt
);
