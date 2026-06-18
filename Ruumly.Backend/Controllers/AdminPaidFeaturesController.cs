using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Controllers;

[Route("api/admin/paid-features")]
public class AdminPaidFeaturesController(RuumlyDbContext db) : AdminBaseController(db)
{
    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalog()
    {
        var features = await Db.PaidFeatures
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.Name)
            .ToListAsync();

        return Ok(features.Select(PaidFeatureMappers.MapFeature).ToList());
    }

    [HttpGet("requests")]
    public async Task<IActionResult> GetRequests([FromQuery] string? status = null)
    {
        var query = Db.PaidFeatureRequests
            .Include(r => r.PaidFeature)
            .Include(r => r.Supplier)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<PaidFeatureRequestStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(r => r.Status == parsedStatus);
        }

        var requests = await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(200)
            .ToListAsync();

        return Ok(requests.Select(PaidFeatureMappers.MapRequest).ToList());
    }

    [HttpPost("requests/{id:guid}/activate")]
    public async Task<IActionResult> ActivateRequest(Guid id, [FromBody] ActivatePaidFeatureRequest body)
    {
        var request = await Db.PaidFeatureRequests
            .Include(r => r.PaidFeature)
            .Include(r => r.Supplier)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request is null)
            return NotFound(Error("Paid feature request not found."));

        var now = DateTime.UtcNow;
        var startsAt = body.StartsAt ?? now;

        var existingActivation = await Db.SupplierPaidFeatures
            .Include(f => f.PaidFeature)
            .FirstOrDefaultAsync(f =>
                f.SupplierId == request.SupplierId &&
                f.PaidFeatureId == request.PaidFeatureId &&
                f.ListingId == request.ListingId &&
                f.LocationId == request.LocationId &&
                f.IsActive);

        SupplierPaidFeature activation;
        if (existingActivation is null)
        {
            activation = new SupplierPaidFeature
            {
                Id = Guid.NewGuid(),
                SupplierId = request.SupplierId,
                PaidFeatureId = request.PaidFeatureId,
                ListingId = request.ListingId,
                LocationId = request.LocationId,
                StartsAt = startsAt,
                EndsAt = body.EndsAt,
                IsActive = true,
                AdminNotes = body.AdminNotes,
                CreatedAt = now,
                UpdatedAt = now,
                PaidFeature = request.PaidFeature,
            };
            Db.SupplierPaidFeatures.Add(activation);
        }
        else
        {
            activation = existingActivation;
            activation.StartsAt = startsAt;
            activation.EndsAt = body.EndsAt;
            activation.AdminNotes = body.AdminNotes;
            activation.UpdatedAt = now;
        }

        ApplyCommerceCapability(request.Supplier, request.PaidFeature.Code);

        request.Status = PaidFeatureRequestStatus.Activated;
        request.AdminNotes = body.AdminNotes;
        request.ReviewedByUserId = User.GetUserId();
        request.UpdatedAt = now;

        Audit(
            "paid_feature.activated",
            User.GetUserEmail(),
            request.Supplier.Name,
            $"{request.PaidFeature.Code} activated for supplier {request.SupplierId}");

        await Db.SaveChangesAsync();

        return Ok(PaidFeatureMappers.MapActivation(activation));
    }

    [HttpPost("requests/{id:guid}/decline")]
    public async Task<IActionResult> DeclineRequest(Guid id, [FromBody] DeclinePaidFeatureRequest body)
    {
        var request = await Db.PaidFeatureRequests
            .Include(r => r.PaidFeature)
            .Include(r => r.Supplier)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request is null)
            return NotFound(Error("Paid feature request not found."));

        request.Status = PaidFeatureRequestStatus.Dismissed;
        request.AdminNotes = body.AdminNotes;
        request.ReviewedByUserId = User.GetUserId();
        request.UpdatedAt = DateTime.UtcNow;

        Audit(
            "paid_feature.declined",
            User.GetUserEmail(),
            request.Supplier.Name,
            $"{request.PaidFeature.Code} declined for supplier {request.SupplierId}");

        await Db.SaveChangesAsync();

        return Ok(PaidFeatureMappers.MapRequest(request));
    }

    [HttpPost("catalog")]
    public async Task<IActionResult> CreateFeature([FromBody] UpsertPaidFeatureRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Code) || string.IsNullOrWhiteSpace(body.Name))
            return BadRequest(Error("Code and Name are required."));

        var code = body.Code.Trim();
        if (await Db.PaidFeatures.AnyAsync(f => f.Code == code))
            return BadRequest(Error($"A paid feature with code '{code}' already exists."));

        var now = DateTime.UtcNow;
        var feature = new PaidFeature
        {
            Id              = Guid.NewGuid(),
            Code            = code,
            Name            = body.Name.Trim(),
            Description     = body.Description,
            Category        = ParseEnum(body.Category, PaidFeatureCategory.Visibility),
            Scope           = ParseEnum(body.Scope, PaidFeatureScope.Supplier),
            PriceAmount     = body.PriceAmount ?? 0m,
            PriceCurrency   = string.IsNullOrWhiteSpace(body.PriceCurrency) ? "EUR" : body.PriceCurrency.Trim(),
            BillingInterval = string.IsNullOrWhiteSpace(body.BillingInterval) ? "manual" : body.BillingInterval.Trim(),
            IsActive        = body.IsActive ?? true,
            SortOrder       = body.SortOrder ?? 0,
            CreatedAt       = now,
            UpdatedAt       = now,
        };

        Db.PaidFeatures.Add(feature);
        Audit("paid_feature.catalog_created", User.GetUserEmail(), feature.Code, feature.Name);
        await Db.SaveChangesAsync();

        return Ok(PaidFeatureMappers.MapFeature(feature));
    }

    [HttpPatch("catalog/{id:guid}")]
    public async Task<IActionResult> UpdateFeature(Guid id, [FromBody] UpsertPaidFeatureRequest body)
    {
        var feature = await Db.PaidFeatures.FirstOrDefaultAsync(f => f.Id == id);
        if (feature is null)
            return NotFound(Error("Paid feature not found."));

        if (!string.IsNullOrWhiteSpace(body.Name))            feature.Name            = body.Name.Trim();
        if (body.Description is not null)                     feature.Description     = body.Description;
        if (!string.IsNullOrWhiteSpace(body.Category))        feature.Category        = ParseEnum(body.Category, feature.Category);
        if (!string.IsNullOrWhiteSpace(body.Scope))           feature.Scope           = ParseEnum(body.Scope, feature.Scope);
        if (body.PriceAmount.HasValue)                        feature.PriceAmount     = body.PriceAmount.Value;
        if (!string.IsNullOrWhiteSpace(body.PriceCurrency))   feature.PriceCurrency   = body.PriceCurrency.Trim();
        if (!string.IsNullOrWhiteSpace(body.BillingInterval)) feature.BillingInterval = body.BillingInterval.Trim();
        if (body.IsActive.HasValue)                           feature.IsActive        = body.IsActive.Value;
        if (body.SortOrder.HasValue)                          feature.SortOrder       = body.SortOrder.Value;
        feature.UpdatedAt = DateTime.UtcNow;

        Audit("paid_feature.catalog_updated", User.GetUserEmail(), feature.Code,
            $"isActive={feature.IsActive}, price={feature.PriceAmount}");
        await Db.SaveChangesAsync();

        return Ok(PaidFeatureMappers.MapFeature(feature));
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct
        => Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;

    private static void ApplyCommerceCapability(Supplier supplier, string featureCode)
    {
        switch (featureCode)
        {
            case "booking_tools":
                supplier.BookingEnabled = true;
                break;
            case "contract_tools":
                supplier.ContractSigningEnabled = true;
                break;
            case "ruumly_payment_collection":
                supplier.RuumlyPaymentEnabled = true;
                break;
            case "direct_payment_tools":
                supplier.DirectPaymentEnabled = true;
                break;
        }
    }
}
