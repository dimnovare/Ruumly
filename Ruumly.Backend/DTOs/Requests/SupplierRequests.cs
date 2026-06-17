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
    string? ApiAuthToken,
    decimal? PartnerDiscountRate,
    decimal? ClientDiscountRate,
    string? Notes,
    string? Iban,
    string? BankAccountName,
    string? BankName,
    // Optional commerce capabilities
    bool? BookingEnabled,
    bool? ContractSigningEnabled,
    bool? DirectPaymentEnabled,
    bool? RuumlyPaymentEnabled,
    // Partner page fields (all optional — null = leave unchanged)
    string?  Slug,
    bool?    IsPartnerPagePublished,
    string?  Tagline,
    string?  LongDescriptionEt,
    string?  LongDescriptionEn,
    string?  LongDescriptionRu,
    string?  LogoUrl,
    string?  HeroImageUrl,
    string?  WebsiteUrl,
    int?     FoundedYear,
    bool?    FoundingPartner,
    bool?    IsVerified,
    string?  GooglePlaceId,
    // Polling fields
    bool?    PollingEnabled,
    int?     PollingIntervalMinutes,
    string?  PollingEndpoint);

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
