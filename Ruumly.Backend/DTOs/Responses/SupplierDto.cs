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
    string? ApiAuthToken,
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
    int     ListingCount,
    IntegrationSettingsDto? IntegrationSettings,
    string    BillingModel,
    bool      BookingEnabled,
    bool      ContractSigningEnabled,
    bool      DirectPaymentEnabled,
    bool      RuumlyPaymentEnabled,
    string    Tier,
    decimal   CommissionRate,
    decimal   MonthlyFee,
    bool      HasFullAnalytics,
    bool      CanHavePromotedBadge,
    bool      HasCalendarSync,
    bool      FoundingPartner,
    DateTime? OnboardingStartedAt,
    bool      IsInOnboarding,
    int       OnboardingDaysRemaining,
    bool      IsVerified,
    string    PriorityLevel,
    string?   Country,
    // Partner page fields
    string?   Slug,
    bool      IsPartnerPagePublished,
    /// <summary>
    /// True for a row Ruumly created from public research rather than a partner
    /// who signed up. The provider dashboard reads this to decide what to show:
    /// a claimed directory listing has no bookings, payouts, contracts or
    /// calendar, so surfacing those sections gives them a control panel for a
    /// business relationship that does not exist.
    /// </summary>
    bool      IsDirectoryListing,
    string?   Tagline,
    string?   LongDescriptionEt,
    string?   LongDescriptionEn,
    string?   LongDescriptionRu,
    string?   LogoUrl,
    string?   HeroImageUrl,
    string?   WebsiteUrl,
    int?      FoundedYear,
    string?   GooglePlaceId,
    string?   PartnerPageUrl,  // null when no slug, else "/partner/{slug}"
    // Polling fields
    bool      PollingEnabled,
    int       PollingIntervalMinutes,
    string?   NextPollAt,      // ISO-8601, null if not yet scheduled
    string?   LastPolledAt,    // ISO-8601, null if never polled
    string?   LastPollStatus,  // "ok" | "error" | null
    string?   PollingEndpoint, // override URL; falls back to ApiEndpoint when null
    // Email deliverability — written by the Resend bounce webhook. True means the
    // address is dead: outreach skips this partner until an admin saves a new one.
    bool      ContactEmailUnusable = false,
    DateTime? ContactEmailBouncedAt = null,
    string?   ContactEmailBounceType = null,    // "hard" | "soft" | "complaint"
    string?   ContactEmailBounceReason = null,
    // What the partner sells. Empty means the partner can never be returned as a
    // candidate for any lead — the admin list flags it for exactly that reason.
    IReadOnlyList<string>? ServiceTypes = null
);
