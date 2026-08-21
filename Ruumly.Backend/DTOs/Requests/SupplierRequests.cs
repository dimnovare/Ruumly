using System.Text.Json.Serialization;

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
    bool? RuumlyPaymentEnabled,
    // What the partner actually SELLS. Required: ProviderCandidateFinder filters on
    // Supplier.ServiceTypesJson before location or radius, so a partner created
    // without any is invisible to every lead while looking complete in admin.
    List<string>? ServiceTypes = null,
    string? Country = null);

/// <summary>
/// PATCH /api/admin/suppliers/{id}.
///
/// <see cref="JsonUnmappedMemberHandling.Disallow"/> is deliberate: this endpoint
/// used to accept <c>serviceTypes</c>, <c>country</c> and <c>isActive</c>, answer
/// 200, and persist none of them. An endpoint that swallows a field it does not
/// write is worse than one that rejects it, because the caller believes the write
/// landed. Anything unmapped is now a 400 naming the property.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
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
    // Who this provider will work for. Null leaves it unchanged; both default
    // to true on the row, so an untouched supplier keeps receiving everything.
    // Set from what a provider actually TELLS us in a refusal, never guessed.
    bool? ServesConsumers = null,
    bool? ServesRecurring = null,
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
    string?  PollingEndpoint = null,
    // What the partner sells. null = leave unchanged; an explicit list REPLACES the
    // consumer-facing slugs (stored packing/insurance metadata is preserved).
    List<string>? ServiceTypes = null,
    // Picks the provider-outreach language. The admin profile form has always sent
    // this — it was bound to nothing and silently dropped until now.
    string?  Country = null,
    // Accepted only so the endpoint can REFUSE it by name: marketplace visibility
    // belongs to PATCH /admin/suppliers/{id}/status, whose behaviour is unchanged.
    bool?    IsActive = null);

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
