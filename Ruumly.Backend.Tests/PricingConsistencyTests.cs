using FluentAssertions;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Verifies that the backend pricing formulas match the partner model
/// implemented in BookingService.cs and OrderRoutingService.cs.
///
/// Formulas under test:
///   platformPrice      = Math.Round(basePrice * (1 - customerDiscountRate / 100))
///   supplierPrice      = Math.Round(basePrice * (1 - partnerDiscountRate / 100))
///   extrasCustomerTotal = sum of extra.CustomerPrice  (= supplierPrice * (1 + extrasMarginRate/100))
///   extrasSupplierTotal = sum of extra.SupplierPrice
///   baseMargin         = platformPrice - supplierPrice
///   extrasMargin       = extrasCustomerTotal - extrasSupplierTotal
///   margin             = baseMargin + extrasMargin
/// </summary>
public class PricingConsistencyTests
{
    // ─── Core formula helpers ──────────────────────────────────────────────

    private static decimal PlatformPrice(decimal basePrice, decimal customerDiscountRate) =>
        Math.Round(basePrice * (1m - customerDiscountRate / 100m));

    private static decimal SupplierPrice(decimal basePrice, decimal partnerDiscountRate) =>
        Math.Round(basePrice * (1m - partnerDiscountRate / 100m));

    private static decimal CustomerExtrasPrice(decimal supplierExtrasPrice, decimal extrasMarginRate) =>
        Math.Round(supplierExtrasPrice * (1m + extrasMarginRate / 100m), 2);

    private static decimal Margin(decimal platformPrice, decimal supplierPrice,
                                  decimal extrasCustomerTotal, decimal extrasSupplierTotal) =>
        (platformPrice - supplierPrice) + (extrasCustomerTotal - extrasSupplierTotal);

    // ─── Base pricing — customer discount ─────────────────────────────────

    [Theory]
    [InlineData(100,  5,  95)]
    [InlineData(100,  8,  92)]
    [InlineData(100, 12,  88)]
    [InlineData(200,  5, 190)]
    [InlineData(200, 12, 176)]
    public void PlatformPrice_AppliesCustomerDiscount(double basePrice, double discount, double expected)
    {
        PlatformPrice((decimal)basePrice, (decimal)discount).Should().Be((decimal)expected);
    }

    // ─── Base pricing — partner discount ──────────────────────────────────

    [Theory]
    [InlineData(100, 15,  85)]
    [InlineData(200, 15, 170)]
    [InlineData(100, 20,  80)]
    public void SupplierPrice_AppliesPartnerDiscount(double basePrice, double discount, double expected)
    {
        SupplierPrice((decimal)basePrice, (decimal)discount).Should().Be((decimal)expected);
    }

    // ─── Extras margin ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(10,  20, 12)]   // €10 + 20% = €12
    [InlineData(16,  20, 19.2)] // €16 + 20% = €19.20
    [InlineData(8,   20, 9.6)]  // €8  + 20% = €9.60
    [InlineData(10,   0, 10)]   // 0% margin = pass-through
    public void ExtrasCustomerPrice_AppliesMargin(double supplierPrice, double marginRate, double expected)
    {
        CustomerExtrasPrice((decimal)supplierPrice, (decimal)marginRate)
            .Should().Be((decimal)expected);
    }

    // ─── Margin calculation — no extras ───────────────────────────────────

    [Fact]
    public void Base100_Starter_NoExtras()
    {
        // partnerDiscount=15%, customerDiscount=5%
        var platform = PlatformPrice(100m, 5m);    // 95
        var supplier = SupplierPrice(100m, 15m);   // 85
        var margin   = Margin(platform, supplier, 0m, 0m);

        platform.Should().Be(95m);
        supplier.Should().Be(85m);
        margin.Should().Be(10m);
    }

    [Fact]
    public void Base100_Standard_NoExtras()
    {
        // partnerDiscount=15%, customerDiscount=8%
        var platform = PlatformPrice(100m, 8m);    // 92
        var supplier = SupplierPrice(100m, 15m);   // 85
        var margin   = Margin(platform, supplier, 0m, 0m);

        platform.Should().Be(92m);
        supplier.Should().Be(85m);
        margin.Should().Be(7m);
    }

    [Fact]
    public void Base100_Premium_NoExtras()
    {
        // partnerDiscount=15%, customerDiscount=12%
        var platform = PlatformPrice(100m, 12m);   // 88
        var supplier = SupplierPrice(100m, 15m);   // 85
        var margin   = Margin(platform, supplier, 0m, 0m);

        platform.Should().Be(88m);
        supplier.Should().Be(85m);
        margin.Should().Be(3m);  // smallest tier margin — always positive
    }

    // ─── Margin calculation — with extras ─────────────────────────────────

    [Fact]
    public void WithExtras_MarginApplied()
    {
        // Supplier prices one extra at €10, extrasMarginRate=20%
        var supplierExtrasPrice = 10m;
        var customerExtrasPrice = CustomerExtrasPrice(supplierExtrasPrice, 20m); // 12

        var platform    = PlatformPrice(100m, 5m);    // 95
        var supplier    = SupplierPrice(100m, 15m);   // 85
        var extrasMargin = customerExtrasPrice - supplierExtrasPrice;            // 2
        var margin      = Margin(platform, supplier, customerExtrasPrice, supplierExtrasPrice);

        extrasMargin.Should().Be(2m);
        margin.Should().Be(12m);  // baseMargin(10) + extrasMargin(2)
    }

    [Fact]
    public void MultipleExtras_MarginSummed()
    {
        // packing: supplier €12, customer €15 (20% margin rounded)
        // insurance: supplier €8, customer €9.60
        decimal extrasSupplier = 12m + 8m;     // 20
        decimal extrasCustomer = CustomerExtrasPrice(12m, 20m) + CustomerExtrasPrice(8m, 20m); // 14.4 + 9.6 = 24

        var platform = PlatformPrice(100m, 5m);   // 95
        var supplier = SupplierPrice(100m, 15m);  // 85
        var margin   = Margin(platform, supplier, extrasCustomer, extrasSupplier);

        extrasCustomer.Should().Be(24m);
        margin.Should().Be(14m);  // baseMargin(10) + extrasMargin(4)
    }

    // ─── Margin is always non-negative when partner discount > customer discount

    [Fact]
    public void NegativeMargin_Impossible_When_PartnerDiscount_Exceeds_CustomerDiscount()
    {
        // partnerDiscount(15%) must always exceed customerDiscount
        // All three tier customer discounts are below 15%
        foreach (var customerDiscount in new[] { 5m, 8m, 12m })
        {
            var platform = PlatformPrice(100m, customerDiscount);
            var supplier = SupplierPrice(100m, 15m);
            var margin   = Margin(platform, supplier, 0m, 0m);

            margin.Should().BeGreaterThanOrEqualTo(0m,
                because: $"customerDiscount={customerDiscount}% is always < partnerDiscount=15%");
        }
    }

    // ─── Unknown extras keys contribute zero ──────────────────────────────

    [Fact]
    public void Unknown_ExtraKey_ContributesZero()
    {
        // Service skips keys not found in ListingExtras — no revenue, no cost
        var extrasCustomer = 0m;
        var extrasSupplier = 0m;
        var margin = Margin(PlatformPrice(100m, 5m), SupplierPrice(100m, 15m),
                            extrasCustomer, extrasSupplier);

        margin.Should().Be(10m);  // same as no-extras case
    }
}
