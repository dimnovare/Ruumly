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
