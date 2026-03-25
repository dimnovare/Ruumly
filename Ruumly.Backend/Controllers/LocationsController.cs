using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/locations")]
[Authorize]
public class LocationsController(RuumlyDbContext db) : ControllerBase
{
    private static object Error(string msg) => new { error = msg };

    // ── GET /api/locations ─────────────────────────────────────────────────────
    // Admin: all locations. Provider: only their supplier's locations.
    [HttpGet]
    [Authorize(Roles = "Admin,Provider")]
    public async Task<IActionResult> GetAll()
    {
        var role = User.GetUserRole();
        IQueryable<SupplierLocation> query = db.SupplierLocations.Include(l => l.Supplier);

        if (role == UserRole.Provider)
        {
            var userId = User.GetUserId();
            var user   = await db.Users.FindAsync(userId);
            if (user?.SupplierId is null)
                return Ok(new List<SupplierLocationDto>());

            query = query.Where(l => l.SupplierId == user.SupplierId);
        }

        var locations = await query.OrderBy(l => l.Name).ToListAsync();
        return Ok(locations.Select(MapToDto));
    }

    // ── GET /api/locations/{id} ────────────────────────────────────────────────
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Provider")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var location = await db.SupplierLocations
            .Include(l => l.Supplier)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (location is null)
            return NotFound(Error("Location not found"));

        if (!await CanAccess(location.SupplierId))
            return Forbid();

        return Ok(MapToDto(location));
    }

    // ── POST /api/locations ────────────────────────────────────────────────────
    [HttpPost]
    [Authorize(Roles = "Admin,Provider")]
    public async Task<IActionResult> Create([FromBody] CreateLocationRequest body)
    {
        // Provider can only create for their own supplier
        if (!await CanAccess(body.SupplierId))
            return Forbid();

        var supplier = await db.Suppliers.FindAsync(body.SupplierId);
        if (supplier is null)
            return NotFound(Error("Supplier not found"));

        // Check tier limit — count active locations, not individual unit listings.
        var activeLocationCount = await db.SupplierLocations
            .CountAsync(l => l.SupplierId == body.SupplierId && l.IsActive);

        var maxAllowed = TierRules.MaxLocations(supplier.Tier);

        if (activeLocationCount >= maxAllowed)
        {
            var tierName = supplier.Tier.ToString();
            return BadRequest(new {
                error =
                    $"Teie {tierName} plaan lubab kuni " +
                    $"{maxAllowed} aktiivset asukohta. " +
                    "Uuendage plaani lisaasukohtade jaoks."
            });
        }

        var location = new SupplierLocation
        {
            Id         = Guid.NewGuid(),
            SupplierId = body.SupplierId,
            Name       = body.Name,
            Address    = body.Address,
            City       = body.City,
            Lat        = body.Lat,
            Lng        = body.Lng,
            Notes      = body.Notes,
            CreatedAt  = DateTime.UtcNow,
            UpdatedAt  = DateTime.UtcNow,
        };

        db.SupplierLocations.Add(location);
        await db.SaveChangesAsync();

        location.Supplier = supplier;
        return CreatedAtAction(nameof(GetById), new { id = location.Id }, MapToDto(location));
    }

    // ── PATCH /api/locations/{id} ──────────────────────────────────────────────
    [HttpPatch("{id:guid}")]
    [Authorize(Roles = "Admin,Provider")]
    public async Task<IActionResult> Patch(Guid id, [FromBody] PatchLocationRequest body)
    {
        var location = await db.SupplierLocations
            .Include(l => l.Supplier)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (location is null)
            return NotFound(Error("Location not found"));

        if (!await CanAccess(location.SupplierId))
            return Forbid();

        if (body.Name    is not null) location.Name    = body.Name;
        if (body.Address is not null) location.Address = body.Address;
        if (body.City    is not null) location.City    = body.City;
        if (body.Lat.HasValue)        location.Lat     = body.Lat.Value;
        if (body.Lng.HasValue)        location.Lng     = body.Lng.Value;
        if (body.Notes   is not null) location.Notes   = body.Notes;

        location.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(MapToDto(location));
    }

    // ── DELETE /api/locations/{id} ─────────────────────────────────────────────
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var location = await db.SupplierLocations.FindAsync(id);
        if (location is null)
            return NotFound(Error("Location not found"));

        db.SupplierLocations.Remove(location);
        await db.SaveChangesAsync();

        return NoContent();
    }

    // ── POST /api/locations/{id}/units ─────────────────────────────────────────
    // Creates a new listing (unit) attached to this location.
    [HttpPost("{id:guid}/units")]
    [Authorize(Roles = "Admin,Provider")]
    public async Task<IActionResult> CreateUnit(Guid id, [FromBody] CreateUnitRequest body)
    {
        var location = await db.SupplierLocations
            .Include(l => l.Supplier)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (location is null)
            return NotFound(Error("Location not found"));

        if (!await CanAccess(location.SupplierId))
            return Forbid();

        var listing = new Listing
        {
            Id            = Guid.NewGuid(),
            SupplierId    = location.SupplierId,
            LocationId    = location.Id,
            Type          = ListingType.Warehouse,
            Title         = body.Title,
            Address       = location.Address,
            City          = location.City,
            Lat           = location.Lat,
            Lng           = location.Lng,
            PriceFrom     = body.PriceFrom,
            PriceUnit     = body.PriceUnit,
            SizeM2        = body.SizeM2,
            QuantityTotal = body.QuantityTotal,
            Description   = body.Description ?? string.Empty,
            VatRate       = body.VatRate,
            PricesIncludeVat = body.PricesIncludeVat,
            IsActive      = true,
            AvailableNow  = true,
            CreatedAt     = DateTime.UtcNow,
            UpdatedAt     = DateTime.UtcNow,
        };

        db.Listings.Add(listing);
        await db.SaveChangesAsync();

        return StatusCode(201, new { listing.Id, listing.Title, listing.LocationId });
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<bool> CanAccess(Guid supplierId)
    {
        var role = User.GetUserRole();
        if (role == UserRole.Admin)
            return true;

        var userId = User.GetUserId();
        var user   = await db.Users.FindAsync(userId);
        return user?.SupplierId == supplierId;
    }

    private static SupplierLocationDto MapToDto(SupplierLocation l) => new(
        Id:         l.Id,
        SupplierId: l.SupplierId,
        Name:       l.Name,
        Address:    l.Address,
        City:       l.City,
        Lat:        l.Lat,
        Lng:        l.Lng,
        Notes:      l.Notes,
        CreatedAt:  l.CreatedAt.ToString("yyyy-MM-dd")
    );
}
