namespace Ruumly.Backend.Services;

public static class PricingHelpers
{
    /// <summary>
    /// Single source of truth for effective discount rates on a listing.
    /// Used by ListingService (DTO building) and BookingService.CreateAsync
    /// (booking math) so frontend display and backend math always match.
    /// </summary>
    public static (decimal Partner, decimal Customer) ComputeEffectiveDiscounts(
        decimal? listingPartnerOverride,
        decimal? listingCustomerOverride,
        decimal? supplierPartnerRate,
        decimal defaultPartnerDiscount,
        decimal ruumlyMinMargin)
    {
        var partner = listingPartnerOverride
            ?? supplierPartnerRate
            ?? defaultPartnerDiscount;
        if (partner == 0) partner = defaultPartnerDiscount;

        var customer = listingCustomerOverride
            ?? Math.Max(0m, partner - ruumlyMinMargin);

        return (partner, customer);
    }
}
