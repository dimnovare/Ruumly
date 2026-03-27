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
                price = e.CustomerPrice,  // customer sees customer price
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

        var config        = await pricingConfigService.GetAsync();
        var customerPrice = Math.Round(body.SupplierPrice * (1m + config.ExtrasMarginRate / 100m), 2);

        var extra = new ListingExtra
        {
            Id            = Guid.NewGuid(),
            ListingId     = listingId,
            Key           = body.Key.Trim().ToLower().Replace(" ", "-"),
            Label         = body.Label.Trim(),
            Description   = body.Description,
            SupplierPrice = body.SupplierPrice,
            CustomerPrice = customerPrice,
            SortOrder     = body.SortOrder ?? 0,
            CreatedAt     = DateTime.UtcNow,
        };

        db.ListingExtras.Add(extra);
        await db.SaveChangesAsync();

        return Ok(new
        {
            extra.Id,
            extra.Key,
            extra.Label,
            extra.SupplierPrice,
            extra.CustomerPrice,
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

        if (body.SupplierPrice.HasValue)
        {
            extra.SupplierPrice = body.SupplierPrice.Value;
            var config = await pricingConfigService.GetAsync();
            extra.CustomerPrice = Math.Round(
                body.SupplierPrice.Value * (1m + config.ExtrasMarginRate / 100m), 2);
        }

        await db.SaveChangesAsync();

        return Ok(new
        {
            extra.Id,
            extra.Key,
            extra.Label,
            extra.SupplierPrice,
            extra.CustomerPrice,
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
