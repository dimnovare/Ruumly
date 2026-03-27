using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Ruumly.Backend.Data;
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
            Starter: new TierConfig(
                CustomerDiscountRate:  D("tier.starter.customerDiscount", 5m),
                MonthlyFee:            D("tier.starter.monthlyFee",       0m),
                MaxLocations:          I("tier.starter.maxLocations",     1),
                CanHavePromotedBadge:  false,
                HasFullAnalytics:      false),
            Standard: new TierConfig(
                CustomerDiscountRate:  D("tier.standard.customerDiscount", 8m),
                MonthlyFee:            D("tier.standard.monthlyFee",       49m),
                MaxLocations:          I("tier.standard.maxLocations",     5),
                CanHavePromotedBadge:  false,
                HasFullAnalytics:      true),
            Premium: new TierConfig(
                CustomerDiscountRate:  D("tier.premium.customerDiscount", 12m),
                MonthlyFee:            D("tier.premium.monthlyFee",       99m),
                MaxLocations:          I("tier.premium.maxLocations",     999),
                CanHavePromotedBadge:  true,
                HasFullAnalytics:      true)
        );

        await cache.SetStringAsync(CacheKey,
            JsonSerializer.Serialize(config),
            new DistributedCacheEntryOptions
            { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) });

        return config;
    }

    public async Task InvalidateCacheAsync()
        => await cache.RemoveAsync(CacheKey);
}
