using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Tests;

public class PaidFeatureRequestFlowTests
{
    private static RuumlyDbContext CreateDb() => TestDbContext.Create();

    [Fact]
    public async Task Provider_Can_Request_Paid_Feature()
    {
        await using var db = CreateDb();
        var (supplier, provider, feature) = await SeedSupplierAndFeatureAsync(db);
        var controller = MakeProviderController(db, provider);

        var result = await controller.CreateRequest(new CreatePaidFeatureRequestRequest(
            feature.Id,
            ListingId: null,
            LocationId: null,
            Message: "Please enable bookings."));

        result.Should().BeOfType<OkObjectResult>();
        var request = await db.PaidFeatureRequests.SingleAsync();
        request.SupplierId.Should().Be(supplier.Id);
        request.PaidFeatureId.Should().Be(feature.Id);
        request.RequestedByUserId.Should().Be(provider.Id);
        request.Status.Should().Be(PaidFeatureRequestStatus.New);
    }

    [Fact]
    public async Task Admin_Activate_Request_Creates_Activation_And_Enables_Booking_Tools()
    {
        await using var db = CreateDb();
        var (supplier, provider, feature) = await SeedSupplierAndFeatureAsync(db);
        var request = new PaidFeatureRequest
        {
            Id = Guid.NewGuid(),
            SupplierId = supplier.Id,
            PaidFeatureId = feature.Id,
            RequestedByUserId = provider.Id,
        };
        db.PaidFeatureRequests.Add(request);
        await db.SaveChangesAsync();
        var admin = new User
        {
            Id = Guid.NewGuid(),
            Name = "Admin",
            Email = "admin@ruumly.eu",
            Role = UserRole.Admin,
            EmailVerified = true,
        };
        var controller = MakeAdminController(db, admin);

        var result = await controller.ActivateRequest(request.Id, new ActivatePaidFeatureRequest(
            StartsAt: null,
            EndsAt: DateTime.UtcNow.AddDays(30),
            AdminNotes: "Approved manually."));

        result.Should().BeOfType<OkObjectResult>();
        var activation = await db.SupplierPaidFeatures.SingleAsync();
        activation.SupplierId.Should().Be(supplier.Id);
        activation.PaidFeatureId.Should().Be(feature.Id);
        activation.IsActive.Should().BeTrue();

        var updatedSupplier = await db.Suppliers.FindAsync(supplier.Id);
        updatedSupplier!.BookingEnabled.Should().BeTrue();

        var updatedRequest = await db.PaidFeatureRequests.FindAsync(request.Id);
        updatedRequest!.Status.Should().Be(PaidFeatureRequestStatus.Activated);
    }

    private static async Task<(Supplier supplier, User provider, PaidFeature feature)> SeedSupplierAndFeatureAsync(
        RuumlyDbContext db)
    {
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Name = "Partner",
            ContactName = "Partner Contact",
            ContactEmail = "partner@ruumly.eu",
            ContactPhone = "+37255555555",
        };
        var provider = new User
        {
            Id = Guid.NewGuid(),
            Name = "Provider",
            Email = "provider@ruumly.eu",
            Role = UserRole.Provider,
            SupplierId = supplier.Id,
            EmailVerified = true,
        };
        var feature = new PaidFeature
        {
            Id = Guid.NewGuid(),
            Code = "booking_tools",
            Name = "Ruumly booking tools",
            Category = PaidFeatureCategory.Commerce,
            Scope = PaidFeatureScope.Supplier,
            IsActive = true,
        };

        db.Suppliers.Add(supplier);
        db.Users.Add(provider);
        db.PaidFeatures.Add(feature);
        await db.SaveChangesAsync();
        return (supplier, provider, feature);
    }

    private static ProviderPaidFeaturesController MakeProviderController(RuumlyDbContext db, User user)
    {
        var http = HttpFor(user);
        return new ProviderPaidFeaturesController(db)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    private static AdminPaidFeaturesController MakeAdminController(RuumlyDbContext db, User user)
    {
        db.Users.Add(user);
        db.SaveChanges();
        var http = HttpFor(user);
        return new AdminPaidFeaturesController(db)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    private static DefaultHttpContext HttpFor(User user) => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
            ],
            authenticationType: "test"))
    };
}
