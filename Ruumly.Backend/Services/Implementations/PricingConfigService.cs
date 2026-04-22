using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Ruumly.Backend.Data;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;
using System.Text.Json;

namespace Ruumly.Backend.Services.Implementations;

public class PricingConfigService(RuumlyDbContext db, IDistributedCache cache) : IPricingConfigService
{
    private const string CacheKey = "platform:pricing-config";

    public async Task<PricingConfig> GetAsync()
    {
        var cached = await cache.GetStringAsync(CacheKey);
        if (cached is not null)
            return JsonSerializer.Deserialize<PricingConfig>(cached)!;

        var settings = await db.PlatformSettings
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        decimal D(string key, decimal fallback) =>
            settings.TryGetValue(key, out var v) && decimal.TryParse(v, out var d) ? d : fallback;

        int I(string key, int fallback) =>
            settings.TryGetValue(key, out var v) && int.TryParse(v, out var i) ? i : fallback;

        var config = new PricingConfig(
            DefaultPartnerDiscountRate: D("defaultPartnerDiscount", 15m),
            DefaultVatRate:             D("defaultVatRate",          24m),
            ExtrasMarginRate:           D("extrasMarginRate",        20m),
            RuumlyMinMarginRate:        D("ruumlyMinMargin",          8m),
            Starter: new TierConfig(
                CustomerDiscountRate:  D("tier.starter.customerDiscount", 5m),
                MonthlyFee:            D("tier.starter.monthlyFee",       0m),
                MaxExtras:             I("tier.starter.maxExtras",        2),
                CanHavePromotedBadge:  false,
                HasFullAnalytics:      false,
                HasCalendarSync:       false,
                SearchPlacement:       "standard",
                SupportLevel:          "email_48h"),
            Standard: new TierConfig(
                CustomerDiscountRate:  D("tier.standard.customerDiscount", 5m),
                MonthlyFee:            D("tier.standard.monthlyFee",       49m),
                MaxExtras:             I("tier.standard.maxExtras",        5),
                CanHavePromotedBadge:  false,
                HasFullAnalytics:      true,
                HasCalendarSync:       false,
                SearchPlacement:       "boosted",
                SupportLevel:          "email_24h"),
            Premium: new TierConfig(
                CustomerDiscountRate:  D("tier.premium.customerDiscount", 5m),
                MonthlyFee:            D("tier.premium.monthlyFee",       99m),
                MaxExtras:             I("tier.premium.maxExtras",        999),
                CanHavePromotedBadge:  true,
                HasFullAnalytics:      true,
                HasCalendarSync:       true,
                SearchPlacement:       "priority",
                SupportLevel:          "priority_4h")
        );

        await cache.SetStringAsync(CacheKey,
            JsonSerializer.Serialize(config),
            new DistributedCacheEntryOptions
            { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) });

        return config;
    }

    public async Task InvalidateCacheAsync()
        => await cache.RemoveAsync(CacheKey);

    public async Task<EffectivePricing> GetEffectivePricingAsync(Models.Supplier supplier)
    {
        var config = await GetAsync();
        var tier   = config.ForTier(supplier.Tier);

        // Onboarding window wins over everything — 0% commission, 0€ fee
        if (supplier.IsInOnboarding)
            return new EffectivePricing(SubscriptionFee: 0m, CommissionRate: 0m);

        // Commission rate: paid tiers buy down commission
        // Founding partners get a permanent 2% discount on whatever the base rate is for their tier
        decimal baseCommission = supplier.Tier switch
        {
            SupplierTier.Premium  => 6m,
            SupplierTier.Standard => 8m,
            _                     => 12m,  // Starter = free tier
        };
        decimal commission = supplier.FoundingPartner
            ? Math.Max(0, baseCommission - 2m)
            : baseCommission;

        // Founding partners get 20% lifetime discount on any paid tier subscription
        decimal subscriptionFee = supplier.FoundingPartner
            ? tier.MonthlyFee * 0.80m
            : tier.MonthlyFee;

        return new EffectivePricing(SubscriptionFee: subscriptionFee, CommissionRate: commission);
    }

    public static decimal GetDefaultVatRate(string? country) => (country ?? "EE") switch
    {
        "EE" => 24m,
        "LV" => 21m,
        "LT" => 21m,
        "FI" => 25.5m,
        "SE" => 25m,
        _    => 24m,
    };
}
