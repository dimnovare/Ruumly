using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/supplier/paid-features")]
[Authorize(Roles = "Provider,Admin")]
public class ProviderPaidFeaturesController(RuumlyDbContext db) : ControllerBase
{
    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalog()
    {
        var features = await db.PaidFeatures
            .Where(f => f.IsActive)
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.Name)
            .ToListAsync();

        return Ok(features.Select(PaidFeatureMappers.MapFeature).ToList());
    }

    [HttpGet]
    public async Task<IActionResult> GetMine()
    {
        var supplierId = await GetSupplierIdAsync();
        if (supplierId is null)
            return NotFound(new { message = "No supplier linked to this account." });

        var now = DateTime.UtcNow;
        var catalogEntities = await db.PaidFeatures
            .Where(f => f.IsActive)
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.Name)
            .ToListAsync();

        var active = await db.SupplierPaidFeatures
            .Include(f => f.PaidFeature)
            .Where(f => f.SupplierId == supplierId.Value &&
                        f.IsActive &&
                        f.StartsAt <= now &&
                        (f.EndsAt == null || f.EndsAt > now))
            .OrderBy(f => f.PaidFeature.SortOrder)
            .ToListAsync();

        var requests = await db.PaidFeatureRequests
            .Include(r => r.PaidFeature)
            .Include(r => r.Supplier)
            .Where(r => r.SupplierId == supplierId.Value)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return Ok(new ProviderPaidFeaturesDto(
            Catalog: catalogEntities.Select(PaidFeatureMappers.MapFeature).ToList(),
            ActiveFeatures: active.Select(PaidFeatureMappers.MapActivation).ToList(),
            Requests: requests.Select(PaidFeatureMappers.MapRequest).ToList()));
    }

    [HttpPost("requests")]
    [EnableRateLimiting("user")]
    public async Task<IActionResult> CreateRequest([FromBody] CreatePaidFeatureRequestRequest body)
    {
        var userId = User.GetUserId();
        var supplierId = await GetSupplierIdAsync();
        if (supplierId is null)
            return NotFound(new { message = "No supplier linked to this account." });

        var feature = await db.PaidFeatures
            .FirstOrDefaultAsync(f => f.Id == body.PaidFeatureId && f.IsActive);
        if (feature is null)
            return NotFound(new { message = "Paid feature not found." });

        if (body.ListingId.HasValue)
        {
            var listingBelongsToSupplier = await db.Listings
                .AnyAsync(l => l.Id == body.ListingId.Value && l.SupplierId == supplierId.Value);
            if (!listingBelongsToSupplier)
                return BadRequest(new { message = "Listing does not belong to this supplier." });
        }

        if (body.LocationId.HasValue)
        {
            var locationBelongsToSupplier = await db.SupplierLocations
                .AnyAsync(l => l.Id == body.LocationId.Value && l.SupplierId == supplierId.Value);
            if (!locationBelongsToSupplier)
                return BadRequest(new { message = "Location does not belong to this supplier." });
        }

        var existingOpenRequest = await db.PaidFeatureRequests
            .Include(r => r.PaidFeature)
            .Include(r => r.Supplier)
            .FirstOrDefaultAsync(r =>
                r.SupplierId == supplierId.Value &&
                r.PaidFeatureId == body.PaidFeatureId &&
                r.ListingId == body.ListingId &&
                r.LocationId == body.LocationId &&
                r.Status == PaidFeatureRequestStatus.New);

        if (existingOpenRequest is not null)
            return Ok(PaidFeatureMappers.MapRequest(existingOpenRequest));

        var request = new PaidFeatureRequest
        {
            Id = Guid.NewGuid(),
            SupplierId = supplierId.Value,
            PaidFeatureId = feature.Id,
            ListingId = body.ListingId,
            LocationId = body.LocationId,
            RequestedByUserId = userId,
            Message = body.Message,
            Status = PaidFeatureRequestStatus.New,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.PaidFeatureRequests.Add(request);
        await db.SaveChangesAsync();

        request.PaidFeature = feature;
        request.Supplier = await db.Suppliers.FindAsync(supplierId.Value) ?? null!;
        return Ok(PaidFeatureMappers.MapRequest(request));
    }

    private async Task<Guid?> GetSupplierIdAsync()
    {
        if (User.IsInRole("Admin"))
        {
            if (Request.Query.TryGetValue("supplierId", out var value) &&
                Guid.TryParse(value, out var supplierId))
                return supplierId;
            return null;
        }

        var userId = User.GetUserId();
        return await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.SupplierId)
            .FirstOrDefaultAsync();
    }
}
