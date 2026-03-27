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
    public void Premium_MaxLocations_Is999() =>
        TierRules.MaxLocations(SupplierTier.Premium)
            .Should().Be(999);

    [Fact]
    public void CustomerDiscountRates_AreCorrect()
    {
        TierRules.CustomerDiscountRate(SupplierTier.Starter)
            .Should().Be(5m);
        TierRules.CustomerDiscountRate(SupplierTier.Standard)
            .Should().Be(8m);
        TierRules.CustomerDiscountRate(SupplierTier.Premium)
            .Should().Be(12m);
    }

    [Fact]
    public void MonthlyFees_AreCorrect()
    {
        TierRules.MonthlyFee(SupplierTier.Starter)
            .Should().Be(0m);
        TierRules.MonthlyFee(SupplierTier.Standard)
            .Should().Be(49m);
        TierRules.MonthlyFee(SupplierTier.Premium)
            .Should().Be(99m);
    }

    [Fact]
    public void CanHavePromotedBadge_OnlyPremium()
    {
        TierRules.CanHavePromotedBadge(SupplierTier.Premium).Should().BeTrue();
        TierRules.CanHavePromotedBadge(SupplierTier.Standard).Should().BeFalse();
        TierRules.CanHavePromotedBadge(SupplierTier.Starter).Should().BeFalse();
    }

    [Fact]
    public void HasFullAnalytics_StandardAndAbove()
    {
        TierRules.HasFullAnalytics(SupplierTier.Premium).Should().BeTrue();
        TierRules.HasFullAnalytics(SupplierTier.Standard).Should().BeTrue();
        TierRules.HasFullAnalytics(SupplierTier.Starter).Should().BeFalse();
    }

    [Fact]
    public void CustomerDiscountRates_AllBelowDefaultPartnerDiscount()
    {
        // Safety invariant: customer discount must always be < default partner discount (15%)
        // otherwise margin would be negative
        const decimal defaultPartnerDiscount = 15m;

        TierRules.CustomerDiscountRate(SupplierTier.Starter)
            .Should().BeLessThan(defaultPartnerDiscount);
        TierRules.CustomerDiscountRate(SupplierTier.Standard)
            .Should().BeLessThan(defaultPartnerDiscount);
        TierRules.CustomerDiscountRate(SupplierTier.Premium)
            .Should().BeLessThan(defaultPartnerDiscount);
    }
}
