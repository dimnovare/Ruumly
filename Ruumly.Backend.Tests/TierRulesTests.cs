using FluentAssertions;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Tests;

public class TierRulesTests
{
    [Fact]
    public void Starter_MaxLocations_Is1() =>
        TierRules.MaxLocations(SupplierTier.Starter)
            .Should().Be(1);

    [Fact]
    public void Standard_MaxLocations_Is5() =>
        TierRules.MaxLocations(SupplierTier.Standard)
            .Should().Be(5);

    [Fact]
    public void Premium_MaxLocations_IsUnlimited() =>
        TierRules.MaxLocations(SupplierTier.Premium)
            .Should().Be(int.MaxValue);

    [Fact]
    public void CommissionRates_AreCorrect()
    {
        TierRules.CommissionRate(SupplierTier.Starter)
            .Should().Be(8m);
        TierRules.CommissionRate(SupplierTier.Standard)
            .Should().Be(5m);
        TierRules.CommissionRate(SupplierTier.Premium)
            .Should().Be(3m);
    }

    [Fact]
    public void MonthlyFees_AreCorrect()
    {
        TierRules.MonthlyFee(SupplierTier.Starter)
            .Should().Be(0m);
        TierRules.MonthlyFee(SupplierTier.Standard)
            .Should().Be(29m);
        TierRules.MonthlyFee(SupplierTier.Premium)
            .Should().Be(79m);
    }
}
