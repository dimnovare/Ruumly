using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Tests;

public class PaidFeatureModelTests
{
    private static RuumlyDbContext CreateDb() => TestDbContext.Create();

    [Fact]
    public async Task PaidFeature_CanBeActivatedForSupplier()
    {
        await using var db = CreateDb();
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Name = "Boost Partner",
            ContactName = "Partner Contact",
            ContactEmail = "partner@ruumly.eu",
            ContactPhone = "+37255555555",
        };
        var feature = new PaidFeature
        {
            Id = Guid.NewGuid(),
            Code = "promoted_search",
            Name = "Promoted search placement",
            Category = PaidFeatureCategory.Visibility,
            Scope = PaidFeatureScope.Supplier,
            PriceAmount = 49m,
            BillingInterval = "monthly",
            IsActive = true,
        };

        db.Suppliers.Add(supplier);
        db.PaidFeatures.Add(feature);
        db.SupplierPaidFeatures.Add(new SupplierPaidFeature
        {
            Id = Guid.NewGuid(),
            SupplierId = supplier.Id,
            PaidFeatureId = feature.Id,
            StartsAt = DateTime.UtcNow.AddDays(-1),
            EndsAt = DateTime.UtcNow.AddDays(29),
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var active = await db.SupplierPaidFeatures
            .Include(s => s.PaidFeature)
            .SingleAsync();

        active.PaidFeature.Code.Should().Be("promoted_search");
        active.PaidFeature.Category.Should().Be(PaidFeatureCategory.Visibility);
        active.IsActive.Should().BeTrue();
    }

    [Fact]
    public void PaidFeatureRequest_DefaultsToNew()
    {
        var request = new PaidFeatureRequest
        {
            Id = Guid.NewGuid(),
            SupplierId = Guid.NewGuid(),
            PaidFeatureId = Guid.NewGuid(),
        };

        request.Status.Should().Be(PaidFeatureRequestStatus.New);
    }
}
