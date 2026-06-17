using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;

namespace Ruumly.Backend.Controllers;

internal static class PaidFeatureMappers
{
    internal static PaidFeatureDto MapFeature(PaidFeature f) => new(
        Id: f.Id,
        Code: f.Code,
        Name: f.Name,
        Description: f.Description,
        Category: f.Category.ToString().ToLowerInvariant(),
        Scope: f.Scope.ToString().ToLowerInvariant(),
        PriceAmount: f.PriceAmount,
        PriceCurrency: f.PriceCurrency,
        BillingInterval: f.BillingInterval,
        IsActive: f.IsActive,
        SortOrder: f.SortOrder);

    internal static SupplierPaidFeatureDto MapActivation(SupplierPaidFeature f) => new(
        Id: f.Id,
        SupplierId: f.SupplierId,
        PaidFeature: MapFeature(f.PaidFeature),
        ListingId: f.ListingId,
        LocationId: f.LocationId,
        IsActive: f.IsActive,
        StartsAt: f.StartsAt,
        EndsAt: f.EndsAt,
        AdminNotes: f.AdminNotes);

    internal static PaidFeatureRequestDto MapRequest(PaidFeatureRequest r) => new(
        Id: r.Id,
        SupplierId: r.SupplierId,
        SupplierName: r.Supplier?.Name,
        PaidFeature: MapFeature(r.PaidFeature),
        ListingId: r.ListingId,
        LocationId: r.LocationId,
        RequestedByUserId: r.RequestedByUserId,
        ReviewedByUserId: r.ReviewedByUserId,
        Status: r.Status.ToString().ToLowerInvariant(),
        Message: r.Message,
        AdminNotes: r.AdminNotes,
        CreatedAt: r.CreatedAt,
        UpdatedAt: r.UpdatedAt);
}
