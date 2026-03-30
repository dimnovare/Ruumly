namespace Ruumly.Backend.DTOs.Requests;

public record CreateSupplierRequest(
    string Name,
    string? BillingModel,
    string? RegistryCode,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone,
    string? IntegrationType,
    string? RecipientEmail,
    string? ApiEndpoint,
    string? ApiAuthType,
    decimal? PartnerDiscountRate,
    decimal? ClientDiscountRate,
    string? Notes,
    string? Iban,
    string? BankAccountName,
    string? BankName);

public record UpdateSupplierRequest(
    string? Name,
    string? BillingModel,
    string? Tier,
    string? RegistryCode,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone,
    string? IntegrationType,
    string? RecipientEmail,
    string? ApiEndpoint,
    string? ApiAuthType,
    decimal? PartnerDiscountRate,
    decimal? ClientDiscountRate,
    string? Notes,
    string? Iban,
    string? BankAccountName,
    string? BankName);
