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
    string? ApiAuthToken,
    decimal? PartnerDiscountRate,
    decimal? ClientDiscountRate,
    string? Notes,
    string? Iban,
    string? BankAccountName,
    string? BankName,
    bool? BookingEnabled,
    bool? ContractSigningEnabled,
    bool? DirectPaymentEnabled,
    bool? RuumlyPaymentEnabled);

public record UpdateSupplierRequest(
    // Core operational fields
    string? Name = null,
    string? BillingModel = null,
    string? Tier = null,
    string? RegistryCode = null,
    string? ContactName = null,
    string? ContactEmail = null,
    string? ContactPhone = null,
    string? IntegrationType = null,
    string? RecipientEmail = null,
    string? ApiEndpoint = null,
    string? ApiAuthType = null,
    string? ApiAuthToken = null,
    decimal? PartnerDiscountRate = null,
    decimal? ClientDiscountRate = null,
    string? Notes = null,
    string? Iban = null,
    string? BankAccountName = null,
    string? BankName = null,
    // Optional commerce capabilities
    bool? BookingEnabled = null,
    bool? ContractSigningEnabled = null,
    bool? DirectPaymentEnabled = null,
    bool? RuumlyPaymentEnabled = null,
    // Partner page fields (all optional — null = leave unchanged)
    string?  Slug = null,
    bool?    IsPartnerPagePublished = null,
    string?  Tagline = null,
    string?  LongDescriptionEt = null,
    string?  LongDescriptionEn = null,
    string?  LongDescriptionRu = null,
    string?  LogoUrl = null,
    string?  HeroImageUrl = null,
    string?  WebsiteUrl = null,
    int?     FoundedYear = null,
    bool?    FoundingPartner = null,
    bool?    IsVerified = null,
    string?  GooglePlaceId = null,
    // Polling fields
    bool?    PollingEnabled = null,
    int?     PollingIntervalMinutes = null,
    string?  PollingEndpoint = null);

public record UpdateSupplierIntegrationRequest(
    // Core integration (mirrors existing UpdateSupplierRequest fields)
    string? IntegrationType,
    string? ApiEndpoint,
    string? ApiAuthType,
    string? ApiAuthToken,
    string? RecipientEmail,
    // IntegrationSettings fields
    string? ApprovalMode,
    string? PostingMode,
    string? FallbackPostingMode,
    // Polling fields (from Supplier model)
    bool?   PollingEnabled,
    int?    PollingIntervalMinutes
);

public record UpdateProviderPartnerPageRequest(
    string? Tagline,
    string? LongDescriptionEt,
    string? LongDescriptionEn,
    string? LongDescriptionRu,
    string? LongDescriptionLv,
    string? LongDescriptionLt,
    string? LogoUrl,
    string? HeroImageUrl,
    string? WebsiteUrl,
    int?    FoundedYear,
    string? GooglePlaceId);
