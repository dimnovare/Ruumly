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
    int     MaxLocations,
    int     MaxActiveUnits,
    int     MaxExtras,
    bool    CanHavePromotedBadge,
    bool    HasFullAnalytics,
    bool    HasCalendarSync,
    string  SearchPlacement,
    string  SupportLevel);

public record PricingConfig(
    decimal DefaultPartnerDiscountRate,
    decimal DefaultVatRate,
    decimal ExtrasMarginRate,
    decimal RuumlyMinMarginRate,
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
