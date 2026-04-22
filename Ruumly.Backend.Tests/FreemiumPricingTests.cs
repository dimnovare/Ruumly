using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;

namespace Ruumly.Backend.Tests;

public class FreemiumPricingTests
{
    private sealed class NoOpCache : IDistributedCache
    {
        public byte[]? Get(string key) => null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.CompletedTask;
    }

    private static Supplier MakeSupplier(
        SupplierTier tier = SupplierTier.Starter,
        bool foundingPartner = false,
        DateTime? onboardingStartedAt = null)
        => new()
        {
            Id = Guid.NewGuid(), Name = "Test", ContactName = "T",
            ContactEmail = "t@t.ee", ContactPhone = "1",
            Tier = tier, FoundingPartner = foundingPartner,
            OnboardingStartedAt = onboardingStartedAt,
        };

    private static PricingConfigService MakeService()
    {
        var db = TestDbContext.Create();
        return new PricingConfigService(db, new NoOpCache());
    }

    [Fact]
    public async Task New_Supplier_Before_Activation_Not_In_Onboarding()
    {
        var supplier = MakeSupplier(onboardingStartedAt: null);
        supplier.IsInOnboarding.Should().BeFalse();
    }

    [Fact]
    public async Task Activated_Supplier_Is_In_Onboarding()
    {
        var supplier = MakeSupplier(onboardingStartedAt: DateTime.UtcNow);
        supplier.IsInOnboarding.Should().BeTrue();

        var svc = MakeService();
        var pricing = await svc.GetEffectivePricingAsync(supplier);
        pricing.SubscriptionFee.Should().Be(0m);
        pricing.CommissionRate.Should().Be(0m);
    }

    [Fact]
    public async Task Onboarding_Ended_Starter_Gets_12pct_Commission()
    {
        var supplier = MakeSupplier(onboardingStartedAt: DateTime.UtcNow.AddDays(-91));
        supplier.IsInOnboarding.Should().BeFalse();

        var svc = MakeService();
        var pricing = await svc.GetEffectivePricingAsync(supplier);
        pricing.SubscriptionFee.Should().Be(0m);
        pricing.CommissionRate.Should().Be(12m);
    }

    [Fact]
    public async Task Founding_Partner_Starter_Gets_10pct_Commission()
    {
        var supplier = MakeSupplier(foundingPartner: true, onboardingStartedAt: DateTime.UtcNow.AddDays(-91));

        var svc = MakeService();
        var pricing = await svc.GetEffectivePricingAsync(supplier);
        pricing.SubscriptionFee.Should().Be(0m);   // Starter fee=0, 0*0.80=0
        pricing.CommissionRate.Should().Be(10m);    // 12 - 2
    }

    [Fact]
    public async Task Founding_Partner_Standard_Gets_Discounted_Fee_And_6pct()
    {
        var supplier = MakeSupplier(
            tier: SupplierTier.Standard,
            foundingPartner: true,
            onboardingStartedAt: DateTime.UtcNow.AddDays(-91));

        var svc = MakeService();
        var pricing = await svc.GetEffectivePricingAsync(supplier);
        pricing.SubscriptionFee.Should().Be(39.20m);  // 49 * 0.80
        pricing.CommissionRate.Should().Be(6m);        // 8 - 2
    }
}
