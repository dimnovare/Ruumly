namespace Ruumly.Backend.DTOs.Responses;

public record SupplierDto(
    Guid    Id,
    string  Name,
    string  RegistryCode,
    string  ContactName,
    string  ContactEmail,
    string  ContactPhone,
    string  IntegrationType,
    string? ApiEndpoint,
    string? ApiAuthType,
    string? RecipientEmail,
    bool    IsActive,
    string  IntegrationHealth,
    decimal PartnerDiscountRate,
    decimal ClientDiscountRate,
    string? Notes,
    string? Iban,
    string? BankAccountName,
    string? BankName,
    string  CreatedAt,
    string  UpdatedAt,
    int     OrdersTotal,
    decimal Revenue,
    IntegrationSettingsDto? IntegrationSettings
);
