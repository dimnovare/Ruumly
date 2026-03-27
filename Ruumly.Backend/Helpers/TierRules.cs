using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// Static fallback values only — the live pricing engine reads from
/// IPricingConfigService (which sources from PlatformSettings).
/// These are used as defaults when the DB/cache is unavailable,
/// and by legacy code paths (AdminMappers, LocationsController) that
/// have not yet been wired to IPricingConfigService.
/// </summary>
/// <remarks>
/// NOTE: In Option C model, customer discount is NOT per-tier.
/// It is calculated as: partnerDiscount - ruumlyMinMargin.
/// These CustomerDiscountRate values are only used as emergency
/// fallbacks if PricingConfigService is unavailable.
/// </remarks>
public static class TierRules
{
    public static decimal CustomerDiscountRate(SupplierTier tier)
        => tier switch
        {
            SupplierTier.Premium  => 12m,
            SupplierTier.Standard => 8m,
            _                     => 5m,
        };

    public static decimal MonthlyFee(SupplierTier tier)
        => tier switch
        {
            SupplierTier.Premium  => 99m,
            SupplierTier.Standard => 49m,
            _                     => 0m,
        };

    public static int MaxLocations(SupplierTier tier)
        => tier switch
        {
            SupplierTier.Premium  => 999,
            SupplierTier.Standard => 5,
            _                     => 1,
        };

    public static bool CanHavePromotedBadge(SupplierTier tier)
        => tier == SupplierTier.Premium;

    public static bool HasFullAnalytics(SupplierTier tier)
        => tier >= SupplierTier.Standard;
}
