using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

/// <summary>
/// Shared DTO-mapping helpers used by all Admin* controllers.
/// </summary>
internal static class AdminMappers
{
    internal static object Error(string message) =>
        new { error = "Not Found", message, statusCode = 404 };

    internal static UserDto MapUser(Models.User u) => new(
        u.Id, u.Name, u.Email, u.Role, u.Status,
        u.Company, u.Phone, u.Avatar,
        u.RegisteredAt, u.LastLoginAt, u.BookingsCount,
        HasGoogleAccount: u.GoogleId is not null,
        SupplierId: u.SupplierId);

    internal static SupplierDto MapSupplier(
        Models.Supplier s, int ordersTotal, decimal revenue, int listingCount,
        bool includeSettings, PricingConfig? pricingConfig = null) => new(
        Id:                  s.Id,
        Name:                s.Name,
        RegistryCode:        s.RegistryCode ?? "",
        ContactName:         s.ContactName,
        ContactEmail:        s.ContactEmail,
        ContactPhone:        s.ContactPhone,
        IntegrationType:     s.IntegrationType.ToString().ToLower(),
        ApiEndpoint:         s.ApiEndpoint,
        ApiAuthType:         s.ApiAuthType,
        ApiAuthToken:        s.ApiAuthToken is not null ? "••••••••" : null,
        RecipientEmail:      s.RecipientEmail,
        IsActive:            s.IsActive,
        IntegrationHealth:   s.IntegrationHealth.ToString().ToLower(),
        PartnerDiscountRate: s.PartnerDiscountRate,
        ClientDiscountRate:  s.ClientDiscountRate,
        Notes:               s.Notes,
        Iban:                s.Iban,
        BankAccountName:     s.BankAccountName,
        BankName:            s.BankName,
        CreatedAt:           s.CreatedAt.ToString("yyyy-MM-dd"),
        UpdatedAt:           s.UpdatedAt.ToString("yyyy-MM-dd"),
        OrdersTotal:         ordersTotal,
        Revenue:             revenue,
        ListingCount:        listingCount,
        IntegrationSettings: includeSettings && s.IntegrationSettings is not null
            ? MapIntegrationSettings(s.IntegrationSettings)
            : null,
        BillingModel:        s.BillingModel.ToString().ToLower(),
        BookingEnabled:      s.BookingEnabled,
        ContractSigningEnabled: s.ContractSigningEnabled,
        DirectPaymentEnabled: s.DirectPaymentEnabled,
        RuumlyPaymentEnabled: s.RuumlyPaymentEnabled,
        Tier:                s.Tier.ToString(),
        CommissionRate:      pricingConfig?.ForTier(s.Tier).CustomerDiscountRate
                             ?? TierRules.CustomerDiscountRate(s.Tier),
        MonthlyFee:          pricingConfig?.ForTier(s.Tier).MonthlyFee
                             ?? TierRules.MonthlyFee(s.Tier),
        HasFullAnalytics:    pricingConfig?.ForTier(s.Tier).HasFullAnalytics
                             ?? TierRules.HasFullAnalytics(s.Tier),
        CanHavePromotedBadge: pricingConfig?.ForTier(s.Tier).CanHavePromotedBadge
                             ?? TierRules.CanHavePromotedBadge(s.Tier),
        HasCalendarSync:     pricingConfig?.ForTier(s.Tier).HasCalendarSync
                             ?? TierRules.HasCalendarSync(s.Tier),
        FoundingPartner:     s.FoundingPartner,
        OnboardingStartedAt: s.OnboardingStartedAt,
        IsInOnboarding:      s.IsInOnboarding,
        OnboardingDaysRemaining: s.OnboardingStartedAt.HasValue
            ? Math.Max(0, 90 - (int)(DateTime.UtcNow - s.OnboardingStartedAt.Value).TotalDays)
            : 0,
        IsVerified:          s.IsVerified,
        PriorityLevel:       s.PriorityLevel.ToString(),
        Country:             s.Country,
        // Partner page fields
        Slug:                     s.Slug,
        IsPartnerPagePublished:   s.IsPartnerPagePublished,
        IsDirectoryListing:       s.IsDirectoryListing,
        Tagline:                  s.Tagline,
        LongDescriptionEt:        ParseLang(s.LongDescriptionTranslationsJson, "et"),
        LongDescriptionEn:        ParseLang(s.LongDescriptionTranslationsJson, "en"),
        LongDescriptionRu:        ParseLang(s.LongDescriptionTranslationsJson, "ru"),
        LogoUrl:                  s.LogoUrl,
        HeroImageUrl:             s.HeroImageUrl,
        WebsiteUrl:               s.WebsiteUrl,
        FoundedYear:              s.FoundedYear,
        GooglePlaceId:            s.GooglePlaceId,
        PartnerPageUrl:           s.Slug != null ? $"/partner/{s.Slug}" : null,
        // Polling fields
        PollingEnabled:           s.PollingEnabled,
        PollingIntervalMinutes:   s.PollingIntervalMinutes,
        NextPollAt:               s.NextPollAt?.ToString("o"),
        LastPolledAt:             s.LastPolledAt?.ToString("o"),
        LastPollStatus:           s.LastPollStatus,
        PollingEndpoint:          s.PollingEndpoint,
        // Deliverability (Resend bounce webhook)
        ContactEmailUnusable:     s.ContactEmailUnusable,
        ServesConsumers:          s.ServesConsumers,
        ServesRecurring:          s.ServesRecurring,
        ContactEmailBouncedAt:    s.ContactEmailBouncedAt,
        ContactEmailBounceType:   s.ContactEmailBounceType,
        ContactEmailBounceReason: s.ContactEmailBounceReason,
        ServiceTypes:             Constants.ServiceCategories.ParseServiceTypes(s.ServiceTypesJson));

    /// <summary>
    /// One row of the admin lead queue (GET /admin/leads).
    ///
    /// Mapped in C# rather than inside the EF projection because of a single
    /// field: photoCount. The photo keys live in a JSON column that LeadPhotos
    /// re-validates on the way out — a parse, not something a database can
    /// express — so the count can only be taken once the row is materialised.
    /// Everything else is byte-for-byte the shape the projection used to emit;
    /// the frontend reads these names.
    ///
    /// The keys themselves are deliberately absent. They address objects in the
    /// PRIVATE bucket, so the admin UI reads a photo by INDEX through
    /// GET /admin/leads/{id}/photos/{index} instead. A count is everything the
    /// list needs to badge a request, and everything the browser is trusted with.
    ///
    /// <paramref name="supplierName"/> is passed in rather than read off the
    /// entity: the routed-partner name comes from a correlated subquery in the
    /// caller's EF query, which a pure mapper has no DbContext to run.
    /// </summary>
    internal static object MapAdminLead(Models.DemandLead d, string? supplierName) => new
    {
        d.Id,
        d.Name,
        d.Email,
        d.Phone,
        d.City,
        category = d.Category.ToString().ToLowerInvariant(),
        d.Query,
        d.Language,
        d.CreatedAt,
        status = d.Status.ToString().ToLowerInvariant(),
        d.AdminNotes,
        // Concierge intake context (null for legacy/routed leads)
        d.ToCity,
        d.NeedDate,
        d.Details,
        d.Source,
        // Which campaign/post/search produced this request. Source says which
        // form; this says what it cost to get someone to it.
        d.Attribution,
        d.ContactedAt,
        // Routing + quote (null for generic demand-capture leads)
        d.SupplierId,
        supplierName,
        d.ListingId,
        d.QuotedPrice,
        d.QuotedAt,
        d.ProviderNotes,
        photoCount = LeadPhotos.Count(d.PhotoKeysJson),
    };

    internal static IntegrationSettingsDto MapIntegrationSettings(Models.IntegrationSettings i) => new(
        Id:                  i.Id,
        SupplierId:          i.SupplierId,
        SupplierName:        i.Supplier?.Name ?? string.Empty,
        ApprovalMode:        i.ApprovalMode.ToString().ToLower(),
        PostingMode:         i.PostingMode.ToString().ToLower(),
        FallbackPostingMode: i.FallbackPostingMode.ToString().ToLower(),
        MappingProfile:      i.MappingProfile,
        PollMappingProfile:  i.PollMappingProfile,
        LastTestedAt:        i.LastTestedAt?.ToString("yyyy-MM-dd HH:mm"),
        LastTestResult:      i.LastTestResult,
        IsActive:            i.IsActive,
        UpdatedAt:           i.UpdatedAt.ToString("yyyy-MM-dd HH:mm"));

    internal static RoutingRuleDto MapRoutingRule(Models.OrderRoutingRule r) => new(
        Id:               r.Id,
        Name:             r.Name,
        SupplierId:       r.SupplierId,
        ServiceType:      r.ServiceType?.ToString().ToLower(),
        OrderType:        r.OrderType,
        PriceThreshold:   r.PriceThreshold,
        CustomerType:     r.CustomerType,
        RequiresApproval: r.RequiresApproval,
        ApproverRole:     r.ApproverRole,
        PostingChannel:   r.PostingChannel.ToString().ToLower(),
        Priority:         r.Priority,
        IsActive:         r.IsActive,
        CreatedAt:        r.CreatedAt.ToString("yyyy-MM-dd"),
        UpdatedAt:        r.UpdatedAt.ToString("yyyy-MM-dd"));

    internal static AuditLogDto MapAuditLog(Models.AuditLog a) => new(
        Id:        a.Id,
        Action:    a.Action,
        Actor:     a.Actor,
        Target:    a.Target,
        Detail:    a.Detail,
        CreatedAt: a.CreatedAt.ToString("yyyy-MM-dd HH:mm"));

    /// <summary>
    /// Parses a single language key out of a LongDescriptionTranslationsJson blob.
    /// Returns null when the JSON is absent, malformed, or the key is missing.
    /// </summary>
    internal static string? ParseLang(string? json, string lang)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var d = System.Text.Json.JsonSerializer
                        .Deserialize<System.Collections.Generic.Dictionary<string, string?>>(json);
            return d?.GetValueOrDefault(lang);
        }
        catch { return null; }
    }

    internal static ListingDto MapListing(Models.Listing l) => new(
        Id:          l.Id,
        Type:        l.Type.ToString().ToLower(),
        Title:       l.Title,
        SupplierName: l.Supplier?.Name ?? string.Empty,
        SupplierSlug: l.Supplier?.Slug,
        Address:     l.Location?.Address ?? l.Address,
        City:        l.Location?.City    ?? l.City,
        Lat:         l.Location?.Lat     ?? l.Lat,
        Lng:         l.Location?.Lng     ?? l.Lng,
        PriceFrom:   l.PriceFrom,
        PriceUnit:   l.PriceUnit,
        AvailableNow: l.AvailableNow,
        Badge:       l.Badge switch
        {
            ListingBadge.Cheapest  => "cheapest",
            ListingBadge.Closest   => "closest",
            ListingBadge.BestValue => "best-value",
            ListingBadge.Promoted  => "promoted",
            _                      => null,
        },
        Rating:      l.Rating,
        ReviewCount: l.ReviewCount,
        Description: l.Description,
        Images:      System.Text.Json.JsonSerializer.Deserialize<List<string>>(l.ImagesJson) ?? [],
        Features:    System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(l.FeaturesJson) ?? [],
        PartnerDiscountRateOverride: l.PartnerDiscountRateOverride,
        ClientDiscountRateOverride:  l.ClientDiscountRateOverride,
        ClientDiscountRate:          l.Supplier?.ClientDiscountRate,
        EffectiveCustomerDiscount:   null,
        EffectivePartnerDiscount:    null,
        VatRate:         l.VatRate,
        PricesIncludeVat: l.PricesIncludeVat,
        DepositAmount:           l.DepositAmount,
        RequiresLicenseCategory: l.RequiresLicenseCategory,
        MinBookingMonths:        l.MinBookingMonths,
        SupplierId:      l.SupplierId,
        SizeM2:          l.SizeM2,
        QuantityTotal:   l.QuantityTotal,
        LocationId:      l.LocationId,
        ViewCount:       l.ViewCount,
        IsVerified:      l.Supplier?.IsVerified ?? false,
        FoundingPartner: l.Supplier?.FoundingPartner ?? false,
        BookingEnabled:  l.Supplier?.BookingEnabled ?? false,
        ContractSigningEnabled: l.Supplier?.ContractSigningEnabled ?? false,
        DirectPaymentEnabled: l.Supplier?.DirectPaymentEnabled ?? false,
        RuumlyPaymentEnabled: l.Supplier?.RuumlyPaymentEnabled ?? false);
}
