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
public class LocationsController(RuumlyDbContext db) : ControllerBase
{
    private static object Error(string msg) => new { error = msg };

    // ── GET /api/locations ─────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? city,
        [FromQuery] string? type)
    {
        var isAuthenticated = User.Identity?.IsAuthenticated ?? false;

        IQueryable<SupplierLocation> query =
            db.SupplierLocations
              .Include(l => l.Supplier)
              .Include(l => l.Listings.Where(u => u.IsActive));

        // Public callers and customers only see active locations
        if (!isAuthenticated || User.GetUserRole() == UserRole.Customer)
            query = query.Where(l => l.IsActive);

        // Providers see only their own supplier's locations
        if (isAuthenticated && User.GetUserRole() == UserRole.Provider)
        {
            var userId = User.GetUserId();
            var user   = await db.Users.FindAsync(userId);
            if (user?.SupplierId is not null)
                query = query.Where(l => l.SupplierId == user.SupplierId);
        }

        if (!string.IsNullOrWhiteSpace(city))
            query = query.Where(l => l.City.ToLower().Contains(city.ToLower()));

        if (!string.IsNullOrWhiteSpace(type) &&
            Enum.TryParse<ListingType>(type, true, out var lt))
            query = query.Where(l => l.Listings.Any(u => u.Type == lt && u.IsActive));

        var locations = await query
            .OrderBy(l => l.City)
            .ThenBy(l => l.Name)
            .ToListAsync();

        return Ok(locations.Select(MapToDto));
    }

    // ── GET /api/locations/{id} ────────────────────────────────────────────────
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var location = await db.SupplierLocations
            .Include(l => l.Supplier)
            .Include(l => l.Listings.Where(u => u.IsActive))
            .FirstOrDefaultAsync(l => l.Id == id);

        if (location is null)
            return NotFound(new { error = ErrorMessages.Get("LOCATION_NOT_FOUND", Request.GetLang()) });

        // Providers can only see their own locations
        if (User.Identity?.IsAuthenticated == true &&
            User.GetUserRole() == UserRole.Provider)
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
            return NotFound(new { error = ErrorMessages.Get("LISTING_NOT_FOUND", Request.GetLang()) });

        // Check tier limit — count active locations, not individual unit listings.
        var activeLocationCount = await db.SupplierLocations
            .CountAsync(l => l.SupplierId == body.SupplierId && l.IsActive);

        var maxAllowed = TierRules.MaxLocations(supplier.Tier);

        if (activeLocationCount >= maxAllowed)
        {
            var lang = Request.GetLang();
            var raw  = ErrorMessages.Get("TIER_LOCATION_LIMIT", lang);
            var msg  = string.Format(raw, maxAllowed);
            return BadRequest(new { error = msg });
        }

        var location = new SupplierLocation
        {
            Id           = Guid.NewGuid(),
            SupplierId   = body.SupplierId,
            Name         = body.Name,
            Address      = body.Address,
            City         = body.City,
            Lat          = body.Lat,
            Lng          = body.Lng,
            Notes        = body.Notes,
            Description  = body.Description ?? string.Empty,
            OpeningHours = body.OpeningHours,
            ImagesJson   = System.Text.Json.JsonSerializer.Serialize(body.Images ?? []),
            CreatedAt    = DateTime.UtcNow,
            UpdatedAt    = DateTime.UtcNow,
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
            .Include(l => l.Listings.Where(u => u.IsActive))
            .FirstOrDefaultAsync(l => l.Id == id);

        if (location is null)
            return NotFound(new { error = ErrorMessages.Get("LOCATION_NOT_FOUND", Request.GetLang()) });

        if (!await CanAccess(location.SupplierId))
            return Forbid();

        if (body.Name        is not null) location.Name        = body.Name;
        if (body.Address     is not null) location.Address     = body.Address;
        if (body.City        is not null) location.City        = body.City;
        if (body.Lat.HasValue)            location.Lat         = body.Lat.Value;
        if (body.Lng.HasValue)            location.Lng         = body.Lng.Value;
        if (body.Notes       is not null) location.Notes       = body.Notes;
        if (body.Description is not null) location.Description = body.Description;
        if (body.OpeningHours is not null) location.OpeningHours = body.OpeningHours;
        if (body.Images      is not null) location.Images      = body.Images;

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
            return NotFound(new { error = ErrorMessages.Get("LOCATION_NOT_FOUND", Request.GetLang()) });

        db.SupplierLocations.Remove(location);
        await db.SaveChangesAsync();

        return NoContent();
    }

    // ── POST /api/locations/{id}/units ─────────────────────────────────────────
    [HttpPost("{id:guid}/units")]
    [Authorize(Roles = "Admin,Provider")]
    public async Task<IActionResult> CreateUnit(Guid id, [FromBody] CreateUnitRequest body)
    {
        var location = await db.SupplierLocations
            .Include(l => l.Supplier)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (location is null)
            return NotFound(new { error = ErrorMessages.Get("LOCATION_NOT_FOUND", Request.GetLang()) });

        if (!await CanAccess(location.SupplierId))
            return Forbid();

        var listing = new Listing
        {
            Id               = Guid.NewGuid(),
            SupplierId       = location.SupplierId,
            LocationId       = location.Id,
            Type             = body.Type,
            Title            = body.Title,
            Address          = location.Address,
            City             = location.City,
            Lat              = location.Lat,
            Lng              = location.Lng,
            PriceFrom        = body.PriceFrom,
            PriceUnit        = body.PriceUnit,
            SizeM2           = body.SizeM2,
            QuantityTotal    = body.QuantityTotal,
            Description      = body.Description ?? string.Empty,
            VatRate          = body.VatRate,
            PricesIncludeVat = body.PricesIncludeVat,
            IsActive         = true,
            AvailableNow     = true,
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow,
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
        Id:           l.Id,
        SupplierId:   l.SupplierId,
        Name:         l.Name,
        Address:      l.Address,
        City:         l.City,
        Lat:          l.Lat,
        Lng:          l.Lng,
        Notes:        l.Notes,
        Images:       l.Images,
        Description:  l.Description,
        OpeningHours: l.OpeningHours,
        UnitCount:    l.Listings.Count(u => u.IsActive),
        PriceFrom:    l.Listings.Where(u => u.IsActive).Select(u => (decimal?)u.PriceFrom).Min(),
        CreatedAt:    l.CreatedAt.ToString("yyyy-MM-dd")
    );
}
