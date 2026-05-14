namespace Ruumly.Backend.DTOs.Responses;

public record SupplierIntegrationDto(
    // From Supplier
    string   IntegrationType,
    string?  ApiEndpoint,
    string?  ApiAuthType,
    bool     HasApiToken,          // true if ApiAuthToken is set; never expose the actual value
    string?  RecipientEmail,
    string   IntegrationHealth,
    // From IntegrationSettings
    Guid     IntegrationSettingsId,
    string   ApprovalMode,
    string   PostingMode,
    string   FallbackPostingMode,
    // Polling
    bool     PollingEnabled,
    int      PollingIntervalMinutes,
    string?  NextPollAt,
    string?  LastPolledAt,
    string?  LastPollStatus
);
