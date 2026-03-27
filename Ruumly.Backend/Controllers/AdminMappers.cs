using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models.Enums;

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
        HasGoogleAccount: u.GoogleId is not null);

    internal static SupplierDto MapSupplier(
        Models.Supplier s, int ordersTotal, decimal revenue, bool includeSettings) => new(
        Id:                  s.Id,
        Name:                s.Name,
        RegistryCode:        s.RegistryCode,
        ContactName:         s.ContactName,
        ContactEmail:        s.ContactEmail,
        ContactPhone:        s.ContactPhone,
        IntegrationType:     s.IntegrationType.ToString().ToLower(),
        ApiEndpoint:         s.ApiEndpoint,
        ApiAuthType:         s.ApiAuthType,
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
        IntegrationSettings: includeSettings && s.IntegrationSettings is not null
            ? MapIntegrationSettings(s.IntegrationSettings)
            : null,
        Tier:                s.Tier.ToString(),
        CommissionRate:      TierRules.CustomerDiscountRate(s.Tier),
        MonthlyFee:          TierRules.MonthlyFee(s.Tier),
        MaxLocations:        TierRules.MaxLocations(s.Tier),
        HasFullAnalytics:    TierRules.HasFullAnalytics(s.Tier),
        CanHavePromotedBadge: TierRules.CanHavePromotedBadge(s.Tier),
        SubscriptionEndsAt:  s.SubscriptionEndsAt);

    internal static IntegrationSettingsDto MapIntegrationSettings(Models.IntegrationSettings i) => new(
        Id:                  i.Id,
        SupplierId:          i.SupplierId,
        SupplierName:        i.Supplier?.Name ?? string.Empty,
        ApprovalMode:        i.ApprovalMode.ToString().ToLower(),
        PostingMode:         i.PostingMode.ToString().ToLower(),
        FallbackPostingMode: i.FallbackPostingMode.ToString().ToLower(),
        MappingProfile:      i.MappingProfile,
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

    internal static ListingDto MapListing(Models.Listing l) => new(
        Id:          l.Id,
        Type:        l.Type.ToString().ToLower(),
        Title:       l.Title,
        SupplierName: l.Supplier?.Name ?? string.Empty,
        Address:     l.Address,
        City:        l.City,
        Lat:         l.Lat,
        Lng:         l.Lng,
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
        VatRate:         l.VatRate,
        PricesIncludeVat: l.PricesIncludeVat,
        SupplierId:      l.SupplierId,
        SizeM2:          l.SizeM2,
        QuantityTotal:   l.QuantityTotal,
        LocationId:      l.LocationId);
}
