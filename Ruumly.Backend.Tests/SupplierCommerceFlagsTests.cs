using FluentAssertions;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Models;

namespace Ruumly.Backend.Tests;

public class SupplierCommerceFlagsTests
{
    [Fact]
    public void MapSupplier_DefaultsCommerceCapabilitiesToDisabled()
    {
        var supplier = new Supplier
        {
            Id           = Guid.NewGuid(),
            Name         = "Directory Partner",
            ContactName  = "Partner Contact",
            ContactEmail = "partner@ruumly.eu",
            ContactPhone = "+37255555555",
        };

        var dto = AdminMappers.MapSupplier(
            supplier,
            ordersTotal: 0,
            revenue: 0m,
            listingCount: 0,
            includeSettings: false);

        dto.BookingEnabled.Should().BeFalse();
        dto.ContractSigningEnabled.Should().BeFalse();
        dto.DirectPaymentEnabled.Should().BeFalse();
        dto.RuumlyPaymentEnabled.Should().BeFalse();
    }

    [Fact]
    public void MapSupplier_ExposesEnabledCommerceCapabilities()
    {
        var supplier = new Supplier
        {
            Id                     = Guid.NewGuid(),
            Name                   = "Commerce Partner",
            ContactName            = "Partner Contact",
            ContactEmail           = "partner@ruumly.eu",
            ContactPhone           = "+37255555555",
            Slug                   = "commerce-partner",
            BookingEnabled         = true,
            ContractSigningEnabled = true,
            DirectPaymentEnabled   = true,
            RuumlyPaymentEnabled   = true,
        };

        var dto = AdminMappers.MapSupplier(
            supplier,
            ordersTotal: 0,
            revenue: 0m,
            listingCount: 0,
            includeSettings: false);

        dto.BookingEnabled.Should().BeTrue();
        dto.ContractSigningEnabled.Should().BeTrue();
        dto.DirectPaymentEnabled.Should().BeTrue();
        dto.RuumlyPaymentEnabled.Should().BeTrue();
    }

    [Fact]
    public void MapListing_ExposesSupplierCommerceCapabilities()
    {
        var supplier = new Supplier
        {
            Id                     = Guid.NewGuid(),
            Name                   = "Commerce Partner",
            ContactName            = "Partner Contact",
            ContactEmail           = "partner@ruumly.eu",
            ContactPhone           = "+37255555555",
            Slug                   = "commerce-partner",
            BookingEnabled         = true,
            ContractSigningEnabled = true,
            DirectPaymentEnabled   = true,
            RuumlyPaymentEnabled   = true,
        };
        var listing = new Listing
        {
            Id           = Guid.NewGuid(),
            SupplierId   = supplier.Id,
            Supplier     = supplier,
            Type         = Ruumly.Backend.Models.Enums.ListingType.Warehouse,
            Title        = "Storage unit",
            Address      = "Test 1",
            City         = "Tallinn",
            PriceFrom    = 100m,
            PriceUnit    = "month",
            AvailableNow = true,
            Description  = "Test",
        };

        var dto = AdminMappers.MapListing(listing);

        dto.SupplierSlug.Should().Be("commerce-partner");
        dto.BookingEnabled.Should().BeTrue();
        dto.ContractSigningEnabled.Should().BeTrue();
        dto.DirectPaymentEnabled.Should().BeTrue();
        dto.RuumlyPaymentEnabled.Should().BeTrue();
    }
}
