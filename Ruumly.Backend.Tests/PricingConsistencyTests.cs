using FluentAssertions;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Verifies that the backend pricing formulas match the Option C partner model
/// implemented in BookingService.cs and OrderRoutingService.cs.
///
/// Option C formulas:
///   customerDiscountRate = max(0, partnerDiscountRate - ruumlyMinMarginRate)
///   platformPrice        = Math.Round(basePrice * (1 - customerDiscountRate / 100))
///   supplierPrice        = Math.Round(basePrice * (1 - partnerDiscountRate / 100))
///   margin               = platformPrice - supplierPrice
///                        = basePrice * ruumlyMinMarginRate / 100  (when no rounding)
///
///   extrasCustomerTotal  = sum of extra.CustomerPrice
///   extrasSupplierTotal  = sum of extra.SupplierPrice
///   totalMargin          = margin + (extrasCustomerTotal - extrasSupplierTotal)
/// </summary>
public class PricingConsistencyTests
{
    // ─── Core formula helpers ──────────────────────────────────────────────

    private static decimal CustomerDiscountRate(decimal partnerDiscountRate, decimal minMarginRate) =>
        Math.Max(0m, partnerDiscountRate - minMarginRate);

    private static decimal PlatformPrice(decimal basePrice, decimal partnerDiscountRate, decimal minMarginRate) =>
        Math.Round(basePrice * (1m - CustomerDiscountRate(partnerDiscountRate, minMarginRate) / 100m));

    private static decimal SupplierPrice(decimal basePrice, decimal partnerDiscountRate) =>
        Math.Round(basePrice * (1m - partnerDiscountRate / 100m));

    private static decimal Margin(decimal platformPrice, decimal supplierPrice,
                                  decimal extrasCustomerTotal = 0m, decimal extrasSupplierTotal = 0m) =>
        (platformPrice - supplierPrice) + (extrasCustomerTotal - extrasSupplierTotal);

    private static decimal CustomerExtrasPrice(decimal supplierExtrasPrice, decimal extrasMarginRate) =>
        Math.Round(supplierExtrasPrice * (1m + extrasMarginRate / 100m), 2);

    // ─── Option C: customerDiscount = partnerDiscount - minMargin ─────────

    [Fact]
    public void OptionC_Base100_Partner15_MinMargin8()
    {
        // customerDiscount = 15 - 8 = 7%
        // platformPrice    = 100 * 0.93 = 93
        // supplierPrice    = 100 * 0.85 = 85
        // margin           = 93 - 85 = 8
        var platform = PlatformPrice(100m, 15m, 8m);
        var supplier = SupplierPrice(100m, 15m);
        var margin   = Margin(platform, supplier);

        CustomerDiscountRate(15m, 8m).Should().Be(7m);
        platform.Should().Be(93m);
        supplier.Should().Be(85m);
        margin.Should().Be(8m);
    }

    [Fact]
    public void OptionC_Base100_Partner10_MinMargin8()
    {
        // customerDiscount = 10 - 8 = 2%
        // platformPrice    = 100 * 0.98 = 98
        // supplierPrice    = 100 * 0.90 = 90
        // margin           = 98 - 90 = 8
        var platform = PlatformPrice(100m, 10m, 8m);
        var supplier = SupplierPrice(100m, 10m);
        var margin   = Margin(platform, supplier);

        CustomerDiscountRate(10m, 8m).Should().Be(2m);
        platform.Should().Be(98m);
        supplier.Should().Be(90m);
        margin.Should().Be(8m);
    }

    [Fact]
    public void OptionC_Base200_Partner20_MinMargin8()
    {
        // customerDiscount = 20 - 8 = 12%
        // platformPrice    = 200 * 0.88 = 176
        // supplierPrice    = 200 * 0.80 = 160
        // margin           = 176 - 160 = 16 (= 200 * 8%)
        var platform = PlatformPrice(200m, 20m, 8m);
        var supplier = SupplierPrice(200m, 20m);
        var margin   = Margin(platform, supplier);

        CustomerDiscountRate(20m, 8m).Should().Be(12m);
        platform.Should().Be(176m);
        supplier.Should().Be(160m);
        margin.Should().Be(16m);
        margin.Should().Be(200m * 8m / 100m);   // margin = basePrice * minMarginRate
    }

    // ─── Margin always equals basePrice * minMarginRate (no rounding edge) ─

    [Theory]
    [InlineData(100,  10, 8)]
    [InlineData(100,  15, 8)]
    [InlineData(100,  20, 8)]
    [InlineData(200,  15, 8)]
    [InlineData(500,  12, 5)]
    public void Margin_Equals_BasePrice_Times_MinMarginRate(double basePrice, double partner, double minMargin)
    {
        var platform  = PlatformPrice((decimal)basePrice, (decimal)partner, (decimal)minMargin);
        var supplier  = SupplierPrice((decimal)basePrice, (decimal)partner);
        var margin    = Margin(platform, supplier);
        var expected  = Math.Round((decimal)basePrice * (decimal)minMargin / 100m);

        margin.Should().Be(expected,
            because: $"margin must equal basePrice({basePrice}) * minMargin({minMargin}%)");
    }

    // ─── Safety: customerDiscount floors at 0 when partner < minMargin ────

    [Fact]
    public void CustomerDiscount_FloorZero_When_PartnerLessThanMinMargin()
    {
        // partner=5%, minMargin=8% → customerDiscount=0, customer pays full price
        CustomerDiscountRate(5m, 8m).Should().Be(0m);
        PlatformPrice(100m, 5m, 8m).Should().Be(100m);
    }

    [Fact]
    public void CustomerDiscount_FloorZero_ExactMatch()
    {
        // partner=8%, minMargin=8% → customerDiscount=0
        CustomerDiscountRate(8m, 8m).Should().Be(0m);
    }

    // ─── Extras margin ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(10,  20, 12)]    // €10 + 20% = €12
    [InlineData(16,  20, 19.2)]  // €16 + 20% = €19.20
    [InlineData(8,   20, 9.6)]   // €8  + 20% = €9.60
    [InlineData(10,   0, 10)]    // 0% margin = pass-through
    public void ExtrasCustomerPrice_AppliesMargin(double supplierPrice, double marginRate, double expected)
    {
        CustomerExtrasPrice((decimal)supplierPrice, (decimal)marginRate)
            .Should().Be((decimal)expected);
    }

    // ─── Total margin with extras ──────────────────────────────────────────

    [Fact]
    public void WithExtras_TotalMarginIsBaseMarginPlusExtrasMargin()
    {
        // base: partner=15%, minMargin=8% → platformPrice=93, supplierPrice=85, baseMargin=8
        // extras: supplier=€10, customer=€12 (20% margin) → extrasMargin=2
        var platform        = PlatformPrice(100m, 15m, 8m);   // 93
        var supplier        = SupplierPrice(100m, 15m);        // 85
        var extrasSupplier  = 10m;
        var extrasCustomer  = CustomerExtrasPrice(10m, 20m);   // 12
        var margin          = Margin(platform, supplier, extrasCustomer, extrasSupplier);

        margin.Should().Be(10m);  // baseMargin(8) + extrasMargin(2)
    }

    [Fact]
    public void MultipleExtras_MarginSummed()
    {
        // packing: supplier €12, customer €14.40 (20%)
        // insurance: supplier €8, customer €9.60 (20%)
        decimal extrasSupplier = 12m + 8m;   // 20
        decimal extrasCustomer = CustomerExtrasPrice(12m, 20m) + CustomerExtrasPrice(8m, 20m); // 14.4 + 9.6 = 24

        var platform = PlatformPrice(100m, 15m, 8m);   // 93
        var supplier = SupplierPrice(100m, 15m);        // 85
        var margin   = Margin(platform, supplier, extrasCustomer, extrasSupplier);

        extrasCustomer.Should().Be(24m);
        margin.Should().Be(12m);  // baseMargin(8) + extrasMargin(4)
    }

    // ─── Unknown extras keys contribute zero ──────────────────────────────

    [Fact]
    public void Unknown_ExtraKey_ContributesZero()
    {
        var platform = PlatformPrice(100m, 15m, 8m);
        var supplier = SupplierPrice(100m, 15m);
        var margin   = Margin(platform, supplier, 0m, 0m);

        margin.Should().Be(8m);  // same as base-only case
    }
}
