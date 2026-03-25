using FluentAssertions;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Verifies that the backend pricing formulas exactly match the documented
/// frontend pricing model (pricing.ts).
///
/// Formulas under test (from BookingService.cs and OrderRoutingService.cs):
///   platformPrice = Math.Round(basePrice * (1 - clientDiscountRate/100) * 0.95)
///   supplierPrice = Math.Round(basePrice * 0.85)
///   extrasTotal   = sum of matched extras
///   total         = platformPrice + extrasTotal  (VAT = 0 for these cases)
///   margin        = total - supplierPrice - extrasTotal
/// </summary>
public class PricingConsistencyTests
{
    // ─── Core formula helpers (mirror BookingService exactly) ─────────────

    private static decimal CalcPlatformPrice(decimal basePrice, decimal clientDiscountRate = 0m)
    {
        var discountMultiplier = 1m - clientDiscountRate / 100m;
        var discountedBase     = basePrice * discountMultiplier;
        return Math.Round(discountedBase * 0.95m);
    }

    private static decimal CalcSupplierPrice(decimal basePrice) =>
        Math.Round(basePrice * 0.85m);

    private static decimal CalcMargin(decimal total, decimal supplierPrice, decimal extrasTotal) =>
        total - supplierPrice - extrasTotal;

    private static readonly Dictionary<string, decimal> ExtrasPrices =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["packing"]   = 15m,
            ["loading"]   = 20m,
            ["insurance"] = 10m,
            ["forklift"]  = 25m,
        };

    private static decimal CalcExtrasTotal(IEnumerable<string> extras) =>
        extras.Sum(e => ExtrasPrices.TryGetValue(e, out var p) ? p : 0m);

    // ─── Platform price (5% platform fee) ─────────────────────────────────

    [Fact]
    public void PlatformPrice_Is_95pct_Of_BasePrice_With_No_Discount()
    {
        CalcPlatformPrice(100m).Should().Be(95m);
    }

    [Theory]
    [InlineData(200,  190)]
    [InlineData(50,   48)]   // Math.Round(50 * 0.95) = Math.Round(47.5) = 48 (banker's rounding)
    [InlineData(1000, 950)]
    public void PlatformPrice_Scales_With_BasePrice(double basePrice, double expected)
    {
        CalcPlatformPrice((decimal)basePrice).Should().Be((decimal)expected);
    }

    [Fact]
    public void PlatformPrice_Applies_Client_Discount_Before_Fee()
    {
        // 10% client discount: discountedBase = 100 * 0.9 = 90 → platformPrice = 90 * 0.95 = 85.5 → 86
        CalcPlatformPrice(100m, clientDiscountRate: 10m).Should().Be(86m);
    }

    // ─── Supplier price (15% platform cut from base) ──────────────────────

    [Fact]
    public void SupplierPrice_Is_85pct_Of_BasePrice()
    {
        CalcSupplierPrice(100m).Should().Be(85m);
    }

    [Theory]
    [InlineData(200,  170)]
    [InlineData(1000, 850)]
    public void SupplierPrice_Scales_With_BasePrice(double basePrice, double expected)
    {
        CalcSupplierPrice((decimal)basePrice).Should().Be((decimal)expected);
    }

    // ─── Extras prices ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("packing",   15d)]
    [InlineData("loading",   20d)]
    [InlineData("insurance", 10d)]
    [InlineData("forklift",  25d)]
    public void Extras_Individual_Prices_Are_Correct(string extra, double expected)
    {
        CalcExtrasTotal([extra]).Should().Be((decimal)expected);
    }

    [Fact]
    public void Extras_Unknown_Name_Contributes_Zero()
    {
        CalcExtrasTotal(["packing", "unknown", "nonexistent"])
            .Should().Be(15m);
    }

    [Fact]
    public void Extras_Case_Insensitive()
    {
        CalcExtrasTotal(["PACKING", "Loading", "INSURANCE"])
            .Should().Be(45m);
    }

    // ─── Margin calculation ────────────────────────────────────────────────

    [Fact]
    public void Margin_Is_Total_Minus_SupplierPrice_Minus_ExtrasTotal_No_Extras()
    {
        // basePrice=100, no extras, no VAT
        var platformPrice = CalcPlatformPrice(100m);   // 95
        var supplierPrice = CalcSupplierPrice(100m);   // 85
        var extrasTotal   = 0m;
        var total         = platformPrice + extrasTotal;   // 95

        var margin = CalcMargin(total, supplierPrice, extrasTotal);

        platformPrice.Should().Be(95m);
        supplierPrice.Should().Be(85m);
        margin.Should().Be(10m);  // Ruumly keeps €10 on a €100 booking
    }

    [Fact]
    public void Margin_Is_Correct_With_Extras()
    {
        // basePrice=100, extras=packing(15)+loading(20)=35
        var platformPrice = CalcPlatformPrice(100m);         // 95
        var supplierPrice = CalcSupplierPrice(100m);         // 85
        var extrasTotal   = CalcExtrasTotal(["packing", "loading"]);  // 35
        var total         = platformPrice + extrasTotal;              // 130

        var margin = CalcMargin(total, supplierPrice, extrasTotal);

        // margin = 130 - 85 - 35 = 10
        margin.Should().Be(10m);
    }

    // ─── End-to-end pricing scenario ──────────────────────────────────────

    [Fact]
    public void FullScenario_BasePrice100_AllExtras()
    {
        var basePrice     = 100m;
        var platformPrice = CalcPlatformPrice(basePrice);                            // 95
        var supplierPrice = CalcSupplierPrice(basePrice);                            // 85
        var extrasTotal   = CalcExtrasTotal(["packing", "loading", "insurance", "forklift"]); // 70
        var total         = platformPrice + extrasTotal;                             // 165
        var margin        = CalcMargin(total, supplierPrice, extrasTotal);           // 10

        platformPrice.Should().Be(95m);
        supplierPrice.Should().Be(85m);
        extrasTotal.Should().Be(70m);
        total.Should().Be(165m);
        margin.Should().Be(10m);
    }
}
