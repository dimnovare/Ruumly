using Asp.Versioning;
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
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/locations")]
public class LocationsController(RuumlyDbContext db, IPricingConfigService pricingConfigService) : ControllerBase
{
    private static object Error(string msg) => new { error = msg };

    // ── GET /api/locations/cities ─────────────────────────────────────────────
    [HttpGet("cities")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCities()
    {
        var cities = await db.SupplierLocations
            .Where(l => l.IsActive)
            .Select(l => new { l.City, l.Country })
            .Distinct()
            .OrderBy(c => c.Country)
            .ThenBy(c => c.City)
            .ToListAsync();

        return Ok(cities);
    }

    // ── GET /api/locations ─────────────────────────────────────────────────────
    [HttpGet]
    [EnableRateLimiting("search")]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? city,
        [FromQuery] string? type,
        [FromQuery] Guid?   supplierId = null,
        [FromQuery] string? availableFrom = null,
        [FromQuery] string? availableTo   = null)
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

        // Admin can filter by supplier
        if (supplierId.HasValue && isAuthenticated && User.GetUserRole() == UserRole.Admin)
            query = query.Where(l => l.SupplierId == supplierId.Value);

        if (!string.IsNullOrWhiteSpace(city))
            query = query.Where(l => l.City.ToLower().Contains(city.ToLower()));

        if (!string.IsNullOrWhiteSpace(type) &&
            Enum.TryParse<ListingType>(type, true, out var lt))
            query = query.Where(l => l.Listings.Any(u => u.Type == lt && u.IsActive));

        var locations = await query
            .OrderBy(l => l.City)
            .ThenBy(l => l.Name)
            .ToListAsync();

        if (DateTime.TryParse(availableFrom, out var from) &&
            DateTime.TryParse(availableTo,   out var to))
        {
            var bookedListingIds = await db.Bookings
                .Where(b => (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Active || b.Status == BookingStatus.Reserved)
                    && b.StartDate < to && (!b.EndDate.HasValue || b.EndDate > from))
                .Select(b => b.ListingId)
                .Distinct()
                .ToListAsync();

            locations = locations
                .Where(l => l.Listings.Any(u => u.IsActive && !bookedListingIds.Contains(u.Id)))
                .ToList();
        }

        var dtos = new List<SupplierLocationDto>();
        foreach (var loc in locations)
        {
            var (available, fullyBooked) = await ComputeAvailability(loc);
            dtos.Add(MapToDto(loc, available, fullyBooked));
        }
        return Ok(dtos);
    }

    // ── GET /api/locations/{id} ────────────────────────────────────────────────
    [HttpGet("{id:guid}")]
    [EnableRateLimiting("search")]
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

        var (available, fullyBooked) = await ComputeAvailability(location);
        return Ok(MapToDto(location, available, fullyBooked));
    }

    // ── POST /api/locations ────────────────────────────────────────────────────
    [HttpPost]
    [Authorize(Roles = "Admin,Provider")]
    public async Task<IActionResult> Create([FromBody] CreateLocationRequest body)
    {
        // Resolve supplierId: admin passes it explicitly, provider auto-resolves from their account
        Guid supplierId;
        if (body.SupplierId.HasValue && body.SupplierId.Value != Guid.Empty)
        {
            supplierId = body.SupplierId.Value;
        }
        else
        {
            var userId      = User.GetUserId();
            var currentUser = await db.Users.FindAsync(userId);
            if (currentUser?.SupplierId is null)
                return BadRequest(new { error = "No supplier linked to your account." });
            supplierId = currentUser.SupplierId.Value;
        }

        if (!await CanAccess(supplierId))
            return Forbid();

        var supplier = await db.Suppliers.FindAsync(supplierId);
        if (supplier is null)
            return NotFound(new { error = ErrorMessages.Get("LISTING_NOT_FOUND", Request.GetLang()) });

        var location = new SupplierLocation
        {
            Id           = Guid.NewGuid(),
            SupplierId   = supplierId,
            Name         = body.Name,
            Address      = body.Address,
            City         = body.City,
            Country      = supplier.Country ?? "EE",
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
        var (availableC, fullyBookedC) = await ComputeAvailability(location);
        return CreatedAtAction(nameof(GetById), new { id = location.Id }, MapToDto(location, availableC, fullyBookedC));
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

        var (availableP, fullyBookedP) = await ComputeAvailability(location);
        return Ok(MapToDto(location, availableP, fullyBookedP));
    }

    // ── DELETE /api/locations/{id} ─────────────────────────────────────────────
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,Provider")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var location = await db.SupplierLocations.FindAsync(id);
        if (location is null)
            return NotFound(new { error = ErrorMessages.Get("LOCATION_NOT_FOUND", Request.GetLang()) });

        if (!await CanAccess(location.SupplierId))
            return Forbid();

        var hasActiveBookings = await db.Bookings
            .AnyAsync(b => b.Listing.LocationId == id
                        && b.Status != BookingStatus.Cancelled
                        && b.Status != BookingStatus.Completed);

        if (hasActiveBookings)
            return BadRequest(new { error = ErrorMessages.Get("LOCATION_HAS_ACTIVE_BOOKINGS", Request.GetLang()) });

        // Deactivate all child listings so they don't become orphans in search
        var childListings = await db.Listings
            .Where(l => l.LocationId == id)
            .ToListAsync();

        foreach (var listing in childListings)
        {
            listing.IsActive  = false;
            listing.UpdatedAt = DateTime.UtcNow;
        }

        db.SupplierLocations.Remove(location);
        await db.SaveChangesAsync();

        return NoContent();
    }

    // ── POST /api/locations/{id}/units ─────────────────────────────────────────
    [HttpPost("{id:guid}/units")]
    [Authorize(Roles = "Admin,Provider")]
    public async Task<IActionResult> CreateUnit(Guid id, [FromBody] CreateLocationListingRequest body)
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
            FeaturesJson     = body.Features is not null
                                   ? System.Text.Json.JsonSerializer.Serialize(body.Features)
                                   : "{}",
            IsActive         = true,
            AvailableNow     = true,
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow,
        };

        db.Listings.Add(listing);
        await db.SaveChangesAsync();

        return StatusCode(201, new { listing.Id, listing.Title, listing.LocationId });
    }

    // ── PATCH /api/locations/{locationId}/units/{listingId} ────────────────────
    [HttpPatch("{locationId:guid}/units/{listingId:guid}")]
    [Authorize(Roles = "Admin,Provider")]
    public async Task<IActionResult> UpdateUnit(Guid locationId, Guid listingId, [FromBody] PatchListingRequest body)
    {
        var listing = await db.Listings
            .Include(l => l.Supplier)
            .FirstOrDefaultAsync(l => l.Id == listingId && l.LocationId == locationId);

        if (listing is null)
            return NotFound(new { error = "Unit not found" });

        if (!await CanAccess(listing.SupplierId))
            return Forbid();

        if (body.Title is not null)          listing.Title         = body.Title;
        if (body.Description is not null)    listing.Description   = body.Description;
        if (body.PriceFrom.HasValue)         listing.PriceFrom     = body.PriceFrom.Value;
        if (body.PriceUnit is not null)      listing.PriceUnit     = body.PriceUnit;
        if (body.SizeM2.HasValue)            listing.SizeM2        = body.SizeM2.Value;
        if (body.QuantityTotal.HasValue)     listing.QuantityTotal = body.QuantityTotal.Value;
        if (body.VatRate.HasValue)           listing.VatRate       = body.VatRate.Value;
        if (body.IsActive.HasValue)          listing.IsActive      = body.IsActive.Value;
        if (body.Images    is not null)      listing.Images    = body.Images;
        if (body.Features  is not null)      listing.Features  = body.Features;

        listing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { listing.Id, listing.Title, listing.PriceFrom });
    }

    // ── DELETE /api/locations/{locationId}/units/{listingId} ───────────────────
    [HttpDelete("{locationId:guid}/units/{listingId:guid}")]
    [Authorize(Roles = "Admin,Provider")]
    public async Task<IActionResult> DeleteUnit(Guid locationId, Guid listingId)
    {
        var listing = await db.Listings
            .FirstOrDefaultAsync(l => l.Id == listingId && l.LocationId == locationId);

        if (listing is null) return NotFound();
        if (!await CanAccess(listing.SupplierId)) return Forbid();

        // Check if there are active bookings
        var hasBookings = await db.Bookings
            .AnyAsync(b => b.ListingId == listingId
                        && b.Status != BookingStatus.Cancelled
                        && b.Status != BookingStatus.Completed);

        if (hasBookings)
            return BadRequest(new { error = ErrorMessages.Get("UNIT_HAS_ACTIVE_BOOKINGS", Request.GetLang()) });

        db.Listings.Remove(listing);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ── GET /api/locations/{id}/blocked-dates ──────────────────────────────────
    [HttpGet("{id:guid}/blocked-dates")]
    [Authorize(Roles = "Admin,Provider")]
    public async Task<IActionResult> GetBlockedDates(
        Guid id,
        [FromQuery] string? from = null,
        [FromQuery] string? to   = null)
    {
        var location = await db.SupplierLocations.FindAsync(id);
        if (location is null)
            return NotFound(new { error = ErrorMessages.Get("LOCATION_NOT_FOUND", Request.GetLang()) });

        if (!await CanAccess(location.SupplierId))
            return Forbid();

        var query = db.BlockedDates.Where(b => b.LocationId == id);

        if (DateOnly.TryParse(from, out var fromDate))
            query = query.Where(b => b.Date >= fromDate);

        if (DateOnly.TryParse(to, out var toDate))
            query = query.Where(b => b.Date <= toDate);

        var dates = await query
            .OrderBy(b => b.Date)
            .Select(b => new { b.Id, b.LocationId, date = b.Date.ToString("yyyy-MM-dd"), b.Reason })
            .ToListAsync();

        return Ok(dates);
    }

    // ── POST /api/locations/{id}/blocked-dates ─────────────────────────────────
    [HttpPost("{id:guid}/blocked-dates")]
    [Authorize(Roles = "Admin,Provider")]
    public async Task<IActionResult> AddBlockedDate(Guid id, [FromBody] BlockDateRequest body)
    {
        var location = await db.SupplierLocations.FindAsync(id);
        if (location is null)
            return NotFound(new { error = ErrorMessages.Get("LOCATION_NOT_FOUND", Request.GetLang()) });

        if (!await CanAccess(location.SupplierId))
            return Forbid();

        if (!DateOnly.TryParse(body.Date, out var date))
            return BadRequest(new { error = "Invalid date format. Use yyyy-MM-dd." });

        var exists = await db.BlockedDates.AnyAsync(b => b.LocationId == id && b.Date == date);
        if (exists)
            return Conflict(new { error = "This date is already blocked." });

        var blocked = new BlockedDate
        {
            Id         = Guid.NewGuid(),
            LocationId = id,
            Date       = date,
            Reason     = body.Reason,
            CreatedAt  = DateTime.UtcNow,
        };

        db.BlockedDates.Add(blocked);
        await db.SaveChangesAsync();

        return StatusCode(201, new { blocked.Id, blocked.LocationId, date = blocked.Date.ToString("yyyy-MM-dd"), blocked.Reason });
    }

    // ── DELETE /api/locations/{id}/blocked-dates/{dateId} ─────────────────────
    [HttpDelete("{id:guid}/blocked-dates/{dateId:guid}")]
    [Authorize(Roles = "Admin,Provider")]
    public async Task<IActionResult> DeleteBlockedDate(Guid id, Guid dateId)
    {
        var blocked = await db.BlockedDates
            .Include(b => b.Location)
            .FirstOrDefaultAsync(b => b.Id == dateId && b.LocationId == id);

        if (blocked is null) return NotFound();

        if (!await CanAccess(blocked.Location.SupplierId))
            return Forbid();

        db.BlockedDates.Remove(blocked);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // POST /api/locations/{id}/units/import
    [HttpPost("{id:guid}/units/import")]
    [Authorize(Roles = "Admin,Provider")]
    public async Task<IActionResult> ImportUnits(Guid id, [FromBody] ImportUnitsRequest body)
    {
        var location = await db.SupplierLocations
            .Include(l => l.Supplier).FirstOrDefaultAsync(l => l.Id == id);
        if (location is null) return NotFound();
        if (!await CanAccess(location.SupplierId)) return Forbid();

        var created = 0;
        foreach (var row in body.Units)
        {
            var listing = new Listing
            {
                Id            = Guid.NewGuid(),
                SupplierId    = location.SupplierId,
                LocationId    = id,
                Type          = Enum.Parse<ListingType>(row.Type, true),
                Title         = row.Title,
                Address       = location.Address,
                City          = location.City,
                Lat           = location.Lat,
                Lng           = location.Lng,
                PriceFrom     = row.PriceFrom,
                PriceUnit     = row.PriceUnit ?? "/month",
                SizeM2        = row.SizeM2,
                QuantityTotal = row.Quantity ?? 1,
                Description   = row.Description ?? "",
                IsActive      = true,
                AvailableNow  = true,
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow,
            };
            db.Listings.Add(listing);
            created++;
        }
        await db.SaveChangesAsync();
        return Ok(new { imported = created });
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

    private async Task<(int available, bool fullyBooked)> ComputeAvailability(SupplierLocation loc)
    {
        var now            = DateTime.UtcNow;
        var activeListings = loc.Listings.Where(u => u.IsActive).ToList();
        if (activeListings.Count == 0) return (0, false);

        int totalAvailable = 0;
        foreach (var listing in activeListings)
        {
            var capacity       = listing.QuantityTotal ?? 1;
            var activeBookings = await db.Bookings.CountAsync(b =>
                b.ListingId == listing.Id &&
                (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Active || b.Status == BookingStatus.Reserved) &&
                b.StartDate <= now &&
                (!b.EndDate.HasValue || b.EndDate.Value > now));
            totalAvailable += Math.Max(0, capacity - activeBookings);
        }
        return (totalAvailable, totalAvailable == 0);
    }

    private static SupplierLocationDto MapToDto(SupplierLocation l, int availableUnits, bool fullyBooked) => new(
        Id:             l.Id,
        SupplierId:     l.SupplierId,
        Name:           l.Name,
        Address:        l.Address,
        City:           l.City,
        Lat:            l.Lat,
        Lng:            l.Lng,
        Notes:          l.Notes,
        Images:         l.Images,
        Description:    l.Description,
        OpeningHours:   l.OpeningHours,
        UnitCount:      l.Listings.Count(u => u.IsActive),
        AvailableUnits: availableUnits,
        FullyBooked:    fullyBooked,
        PriceFrom:      l.Listings.Where(u => u.IsActive).Select(u => (decimal?)u.PriceFrom).Min(),
        CreatedAt:      l.CreatedAt.ToString("yyyy-MM-dd"),
        Units:          l.Listings
                         .Where(u => u.IsActive)
                         .OrderBy(u => u.PriceFrom)
                         .Select(u => new ListingDto(
                             u.Id, u.Type.ToString().ToLower(), u.Title,
                             l.Supplier?.Name ?? "", l.Address, l.City,
                             l.Lat, l.Lng, u.PriceFrom, u.PriceUnit, u.AvailableNow,
                             u.Badge?.ToString().ToLower(), u.Rating, u.ReviewCount,
                             u.Description, u.Images, u.Features,
                             u.PartnerDiscountRateOverride, u.ClientDiscountRateOverride,
                             l.Supplier?.ClientDiscountRate,
                             u.VatRate, u.PricesIncludeVat, u.SupplierId,
                             u.SizeM2, u.QuantityTotal, u.LocationId, u.ViewCount,
                             l.Supplier?.IsVerified ?? false,
                             l.Supplier?.FoundingPartner ?? false))
                         .ToList()
    );
}
