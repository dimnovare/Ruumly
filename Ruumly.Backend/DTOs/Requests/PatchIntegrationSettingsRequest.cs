namespace Ruumly.Backend.DTOs.Requests;

public record PatchIntegrationSettingsRequest(
    string? ApprovalMode        = null,
    string? PostingMode         = null,
    string? FallbackPostingMode = null,
    string? MappingProfile      = null,
    string? PollMappingProfile  = null,
    bool?   IsActive            = null
);
