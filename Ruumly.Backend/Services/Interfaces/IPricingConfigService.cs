using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Services.Interfaces;

public interface IPricingConfigService
{
    Task<PricingConfig> GetAsync();
    /// <summary>Call after admin saves settings to bust the cache</summary>
    Task InvalidateCacheAsync();
}

public record TierConfig(
    decimal CustomerDiscountRate,
    decimal MonthlyFee,
    int MaxLocations,
    bool CanHavePromotedBadge,
    bool HasFullAnalytics);

public record PricingConfig(
    decimal DefaultPartnerDiscountRate,
    decimal DefaultVatRate,
    decimal ExtrasMarginRate,
    TierConfig Starter,
    TierConfig Standard,
    TierConfig Premium)
{
    public TierConfig ForTier(SupplierTier tier) => tier switch
    {
        SupplierTier.Premium  => Premium,
        SupplierTier.Standard => Standard,
        _                     => Starter,
    };
}
