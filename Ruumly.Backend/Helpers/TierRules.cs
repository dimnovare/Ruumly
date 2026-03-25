using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Helpers;

public static class TierRules
{
    /// <summary>
    /// Maximum number of active LOCATIONS per tier.
    /// Unit types within a location are unlimited
    /// on all tiers — only locations are counted.
    /// </summary>
    public static int MaxLocations(SupplierTier tier)
        => tier switch
        {
            SupplierTier.Premium  => int.MaxValue,
            SupplierTier.Standard => 5,
            _                     => 1,   // Starter
        };

    /// <summary>Commission % deducted per booking.</summary>
    public static decimal CommissionRate(SupplierTier tier)
        => tier switch
        {
            SupplierTier.Premium  => 3m,
            SupplierTier.Standard => 5m,
            _                     => 8m,
        };

    /// <summary>Monthly subscription fee in EUR.</summary>
    public static decimal MonthlyFee(SupplierTier tier)
        => tier switch
        {
            SupplierTier.Premium  => 79m,
            SupplierTier.Standard => 29m,
            _                     => 0m,
        };

    /// <summary>
    /// Premium tier can have the "Soovitatud"
    /// (Promoted) badge on their listings.
    /// </summary>
    public static bool CanHavePromotedBadge(SupplierTier tier)
        => tier == SupplierTier.Premium;

    /// <summary>
    /// Standard and Premium see full analytics
    /// in the provider dashboard.
    /// </summary>
    public static bool HasFullAnalytics(SupplierTier tier)
        => tier >= SupplierTier.Standard;
}
