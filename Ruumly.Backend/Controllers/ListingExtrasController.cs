using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

[ApiController]
public class ListingExtrasController(
    RuumlyDbContext db,
    IPricingConfigService pricingConfigService) : ControllerBase
{
    // ── GET /api/listings/{id}/extras — public, used by booking page ──────────
    [HttpGet("/api/listings/{id:guid}/extras")]
    [AllowAnonymous]
    public async Task<IActionResult> GetListingExtras(Guid id)
    {
        var extras = await db.ListingExtras
            .Where(e => e.ListingId == id && e.IsActive)
            .OrderBy(e => e.SortOrder)
            .Select(e => new
            {
                e.Key,
                e.Label,
                e.Description,
                price       = e.CustomerPrice,                   // what customer pays
                publicPrice = e.PublicPrice,                     // supplier's normal price
                savings     = e.PublicPrice - e.CustomerPrice,   // visible savings
            })
            .ToListAsync();

        return Ok(extras);
    }

    // ── POST /api/listings/{listingId}/extras — provider adds an extra ─────────
    [HttpPost("/api/listings/{listingId:guid}/extras")]
    [Authorize(Roles = "Provider,Admin")]
    public async Task<IActionResult> AddExtra(Guid listingId, [FromBody] CreateExtraRequest body)
    {
        var listing = await db.Listings.FindAsync(listingId);
        if (listing is null)
            return NotFound();

        if (!await CanAccess(listing.SupplierId))
            return Forbid();

        var config   = await pricingConfigService.GetAsync();
        var supplier = await db.Suppliers.FindAsync(listing.SupplierId);

        var effectivePartnerDiscount = body.PartnerDiscountRate
            ?? supplier?.PartnerDiscountRate
            ?? config.DefaultPartnerDiscountRate;

        var supplierPrice = Math.Round(
            body.PublicPrice * (1m - effectivePartnerDiscount / 100m), 2);

        decimal customerPrice;
        if (body.CustomerPriceOverride.HasValue)
        {
            customerPrice = body.CustomerPriceOverride.Value;
        }
        else
        {
            var customerDiscount = Math.Max(0,
                effectivePartnerDiscount - config.RuumlyMinMarginRate);
            customerPrice = Math.Round(
                body.PublicPrice * (1m - customerDiscount / 100m), 2);
        }

        var extra = new ListingExtra
        {
            Id                    = Guid.NewGuid(),
            ListingId             = listingId,
            Key                   = body.Key.Trim().ToLower().Replace(" ", "-"),
            Label                 = body.Label.Trim(),
            Description           = body.Description,
            PublicPrice           = body.PublicPrice,
            PartnerDiscountRate   = body.PartnerDiscountRate,
            SupplierPrice         = supplierPrice,
            CustomerPrice         = customerPrice,
            CustomerPriceOverride = body.CustomerPriceOverride,
            SortOrder             = body.SortOrder ?? 0,
            CreatedAt             = DateTime.UtcNow,
        };

        db.ListingExtras.Add(extra);
        await db.SaveChangesAsync();

        return Ok(new
        {
            extra.Id,
            extra.Key,
            extra.Label,
            extra.PublicPrice,
            extra.SupplierPrice,
            extra.CustomerPrice,
            extra.CustomerPriceOverride,
        });
    }

    // ── PATCH /api/listings/extras/{id} — update price/label/status ──────────
    [HttpPatch("/api/listings/extras/{id:guid}")]
    [Authorize(Roles = "Provider,Admin")]
    public async Task<IActionResult> UpdateExtra(Guid id, [FromBody] UpdateExtraRequest body)
    {
        var extra = await db.ListingExtras
            .Include(e => e.Listing)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (extra is null)
            return NotFound();

        if (!await CanAccess(extra.Listing.SupplierId))
            return Forbid();

        if (body.Label is not null)       extra.Label       = body.Label;
        if (body.Description is not null) extra.Description = body.Description;
        if (body.IsActive.HasValue)       extra.IsActive    = body.IsActive.Value;
        if (body.SortOrder.HasValue)      extra.SortOrder   = body.SortOrder.Value;

        if (body.PublicPrice.HasValue)
            extra.PublicPrice = body.PublicPrice.Value;

        if (body.PartnerDiscountRate.HasValue)
            extra.PartnerDiscountRate = body.PartnerDiscountRate.Value == 0
                ? null   // 0 means "use supplier default"
                : body.PartnerDiscountRate.Value;

        if (body.CustomerPriceOverride.HasValue)
            extra.CustomerPriceOverride = body.CustomerPriceOverride.Value == 0
                ? null   // 0 means "clear override, use auto"
                : body.CustomerPriceOverride.Value;

        // Recalculate derived prices whenever anything changes
        var config   = await pricingConfigService.GetAsync();
        var supplier = await db.Suppliers.FindAsync(extra.Listing.SupplierId);

        var effectiveDiscount = extra.PartnerDiscountRate
            ?? supplier?.PartnerDiscountRate
            ?? config.DefaultPartnerDiscountRate;

        extra.SupplierPrice = Math.Round(
            extra.PublicPrice * (1m - effectiveDiscount / 100m), 2);

        if (extra.CustomerPriceOverride.HasValue)
        {
            extra.CustomerPrice = extra.CustomerPriceOverride.Value;
        }
        else
        {
            var custDiscount = Math.Max(0,
                effectiveDiscount - config.RuumlyMinMarginRate);
            extra.CustomerPrice = Math.Round(
                extra.PublicPrice * (1m - custDiscount / 100m), 2);
        }

        await db.SaveChangesAsync();

        return Ok(new
        {
            extra.Id,
            extra.Key,
            extra.Label,
            extra.PublicPrice,
            extra.SupplierPrice,
            extra.CustomerPrice,
            extra.CustomerPriceOverride,
            extra.IsActive,
        });
    }

    // ── DELETE /api/listings/extras/{id} ─────────────────────────────────────
    [HttpDelete("/api/listings/extras/{id:guid}")]
    [Authorize(Roles = "Provider,Admin")]
    public async Task<IActionResult> DeleteExtra(Guid id)
    {
        var extra = await db.ListingExtras
            .Include(e => e.Listing)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (extra is null)
            return NotFound();

        if (!await CanAccess(extra.Listing.SupplierId))
            return Forbid();

        db.ListingExtras.Remove(extra);
        await db.SaveChangesAsync();

        return NoContent();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private async Task<bool> CanAccess(Guid supplierId)
    {
        var role = User.GetUserRole();
        if (role == UserRole.Admin)
            return true;

        var userId = User.GetUserId();
        var user   = await db.Users.FindAsync(userId);
        return user?.SupplierId == supplierId;
    }
}
