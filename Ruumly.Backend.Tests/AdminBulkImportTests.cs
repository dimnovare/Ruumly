using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Admin bulk listing import: valid rows are created, bad rows are reported per
/// row (never aborting the batch), and rows sharing a city reuse one auto-created
/// SupplierLocation.
/// </summary>
public class AdminBulkImportTests
{
    private sealed class NoOpListings : IListingService
    {
        public Task<PaginatedResult<ListingDto>> SearchAsync(ListingSearchRequest filters, string? language = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ListingDto?> GetByIdAsync(Guid id, string? language = null) => throw new NotImplementedException();
        public Task<List<ListingDto>> GetFeaturedAsync(string? language = null) => throw new NotImplementedException();
        public Task InvalidateListingAsync(Guid id) => Task.CompletedTask;
    }

    private sealed class NoOpPricing : IPricingConfigService
    {
        public Task<PricingConfig> GetAsync() => throw new NotImplementedException();
        public Task InvalidateCacheAsync() => Task.CompletedTask;
        public Task<EffectivePricing> GetEffectivePricingAsync(Supplier supplier) => throw new NotImplementedException();
    }

    private static AdminSettingsController MakeController(RuumlyDbContext db) =>
        new(db, new NoOpListings(), new NoOpPricing())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.Email, "admin@ruumly.eu")], "test")),
                },
            },
        };

    private static Guid SeedSupplier(RuumlyDbContext db)
    {
        var s = new Supplier
        {
            Id = Guid.NewGuid(), Name = "Bulk Partner", ContactName = "C",
            ContactEmail = "s@t.ee", ContactPhone = "1", Country = "EE",
        };
        db.Suppliers.Add(s);
        db.SaveChanges();
        return s.Id;
    }

    [Fact]
    public async Task Bulk_CreatesValidRows_ReportsInvalid_AndDedupesLocation()
    {
        var db = TestDbContext.Create();
        var supplierId = SeedSupplier(db);

        var body = new BulkCreateListingsRequest(supplierId, new List<BulkListingRow>
        {
            new("5 m² unit",  "warehouse", "Tallinn", null, 49m, null, 5m,  3,    null),
            new("10 m² unit", "warehouse", "Tallinn", null, 79m, null, 10m, 2,    null),
            new("Bad type",   "spaceship", "Tallinn", null, 99m, null, null, null, null),
            new("No price",   "warehouse", "Tartu",   null, 0m,  null, null, null, null),
        });

        var result = await MakeController(db).BulkCreateListings(body);
        result.Should().BeOfType<OkObjectResult>();

        var listings = await db.Listings.Where(l => l.SupplierId == supplierId).ToListAsync();
        listings.Should().HaveCount(2, "only the two valid rows create listings");
        listings.Select(l => l.Title).Should().Contain(["5 m² unit", "10 m² unit"]);

        var locations = await db.SupplierLocations.Where(l => l.SupplierId == supplierId).ToListAsync();
        locations.Should().HaveCount(1, "both valid Tallinn rows must reuse a single auto-created location");
        listings.Should().OnlyContain(l => l.LocationId == locations[0].Id);
    }

    [Fact]
    public async Task Bulk_UnknownSupplier_ReturnsNotFound()
    {
        var db = TestDbContext.Create();
        var body = new BulkCreateListingsRequest(Guid.NewGuid(), new List<BulkListingRow>
        {
            new("x", "warehouse", "Tallinn", null, 1m, null, null, null, null),
        });

        var result = await MakeController(db).BulkCreateListings(body);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Bulk_EmptyList_ReturnsBadRequest()
    {
        var db = TestDbContext.Create();
        var supplierId = SeedSupplier(db);

        var result = await MakeController(db).BulkCreateListings(
            new BulkCreateListingsRequest(supplierId, []));

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
