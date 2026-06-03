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
public class LocationsController(RuumlyDbContext db) : ControllerBase
{
    private static object Error(string msg) => new { error = msg };

    // ── GET /api/locations/cities ─────────────────────────────────────────────
    [HttpGet("cities")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCities()
    {
        // Pull distinct cities from active Listings (joined to Suppliers for Country).
        // Was previously querying SupplierLocations which is sparsely populated
        // (only multi-listing groups) — gave a near-empty dropdown.
        var cities = await db.Listings
            .Where(l => l.IsActive && l.Supplier != null)
            .Select(l => new { City = l.City, Country = l.Supplier!.Country })
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

        // Public callers and customers only see active, non-synthetic locations.
        // Synthetic Locations are auto-created data shells and have no user
        // content; their listings appear directly in /api/listings search results.
        if (!isAuthenticated || User.GetUserRole() == UserRole.Customer)
            query = query.Where(l => l.IsActive && !l.IsSynthetic);

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

        // Batch booking counts for all listings to avoid N+1 queries
        var allListingIds = locations
            .SelectMany(l => l.Listings.Where(u => u.IsActive).Select(u => u.Id))
            .Distinct().ToList();
        var nowUtc = DateTime.UtcNow;
        Dictionary<Guid, int> bookedCounts = [];
        if (allListingIds.Count > 0)
        {
            bookedCounts = await db.Bookings
                .Where(b => allListingIds.Contains(b.ListingId)
                         && (b.Status == BookingStatus.Confirmed
                             || b.Status == BookingStatus.Active
                             || b.Status == BookingStatus.Reserved)
                         && b.StartDate <= nowUtc
                         && (!b.EndDate.HasValue || b.EndDate.Value > nowUtc))
                .GroupBy(b => b.ListingId)
                .ToDictionaryAsync(g => g.Key, g => g.Count());
        }

        var dtos = new List<SupplierLocationDto>();
        foreach (var loc in locations)
        {
            var (available, fullyBooked) = ComputeAvailability(loc, bookedCounts);
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

        // Public/customer callers can't access synthetic Locations directly;
        // their listings are surfaced via /api/listings search results instead.
        var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
        if ((!isAuthenticated || User.GetUserRole() == UserRole.Customer) && location.IsSynthetic)
            return NotFound(new { error = ErrorMessages.Get("LOCATION_NOT_FOUND", Request.GetLang()) });

        // Providers can only see their own locations
        if (isAuthenticated &&
            User.GetUserRole() == UserRole.Provider)
            if (!await CanAccess(location.SupplierId))
                return Forbid();

        var (available, fullyBooked) = ComputeAvailability(location, await LoadBookedCountsAsync(location));
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
        var (availableC, fullyBookedC) = ComputeAvailability(location, await LoadBookedCountsAsync(location));
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
        if (body.ExternalId  is not null)
            location.ExternalId = string.IsNullOrWhiteSpace(body.ExternalId)
                ? null : body.ExternalId;

        location.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var (availableP, fullyBookedP) = ComputeAvailability(location, await LoadBookedCountsAsync(location));
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
        if (body.DescriptionTranslations is not null) listing.DescriptionTranslations = body.DescriptionTranslations;
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

    // ── PATCH /api/locations/{id}/publish ─────────────────────────────────────
    /// <summary>
    /// Publishes a location (sets IsActive = true).
    /// Requires at least 1 active unit with PriceFrom > 0.
    /// </summary>
    [HttpPatch("{id:guid}/publish")]
    [Authorize(Roles = "Admin,Provider")]
    public async Task<IActionResult> Publish(Guid id)
    {
        var location = await db.SupplierLocations
            .Include(l => l.Listings)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (location is null)
            return NotFound(new { error = ErrorMessages.Get("LOCATION_NOT_FOUND", Request.GetLang()) });

        if (!await CanAccess(location.SupplierId))
            return Forbid();

        var hasUnit = location.Listings.Any(u => u.IsActive && u.PriceFrom > 0);
        if (!hasUnit)
            return BadRequest(new { error = "At least 1 active unit with a price is required before publishing." });

        location.IsActive  = true;
        location.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { id = location.Id, isActive = location.IsActive });
    }

    // ── PATCH /api/locations/{id}/unpublish ───────────────────────────────────
    /// <summary>Sets IsActive = false, hiding the location from public search.</summary>
    [HttpPatch("{id:guid}/unpublish")]
    [Authorize(Roles = "Admin,Provider")]
    public async Task<IActionResult> Unpublish(Guid id)
    {
        var location = await db.SupplierLocations
            .FirstOrDefaultAsync(l => l.Id == id);

        if (location is null)
            return NotFound(new { error = ErrorMessages.Get("LOCATION_NOT_FOUND", Request.GetLang()) });

        if (!await CanAccess(location.SupplierId))
            return Forbid();

        location.IsActive  = false;
        location.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { id = location.Id, isActive = location.IsActive });
    }

    // ── GET /api/locations/{id}/publish-readiness ─────────────────────────────
    /// <summary>
    /// Returns a checklist of what is blocking a location from going live.
    /// Used by the provider dashboard to guide setup completion.
    /// </summary>
    [HttpGet("{id:guid}/publish-readiness")]
    [Authorize(Roles = "Admin,Provider")]
    public async Task<IActionResult> PublishReadiness(Guid id)
    {
        var location = await db.SupplierLocations
            .Include(l => l.Listings)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (location is null)
            return NotFound(new { error = ErrorMessages.Get("LOCATION_NOT_FOUND", Request.GetLang()) });

        if (!await CanAccess(location.SupplierId))
            return Forbid();

        var activeUnits = location.Listings.Where(u => u.IsActive).ToList();
        var pricedUnits = activeUnits.Where(u => u.PriceFrom > 0).ToList();
        var images      = location.Images;

        var blockers = new List<string>();
        var warnings = new List<string>();

        if (pricedUnits.Count == 0)
            blockers.Add("At least 1 unit with a price is required.");

        if (images.Count == 0)
            blockers.Add("Location needs at least 1 image.");

        if (string.IsNullOrWhiteSpace(location.Description))
            blockers.Add("A description is required.");

        if (images.Count is > 0 and < 3)
            warnings.Add("Adding more images (3+) improves booking conversion.");

        if (activeUnits.Count > 0 && activeUnits.All(u => string.IsNullOrWhiteSpace(u.Description)))
            warnings.Add("Adding descriptions to units helps customers choose the right space.");

        return Ok(new
        {
            locationId = location.Id,
            canPublish = blockers.Count == 0,
            blockers,
            warnings,
        });
    }

    // ── PATCH /api/locations/{locationId}/units/{unitId}/activate ─────────────
    /// <summary>Activates an individual unit without affecting the location's published state.</summary>
    [HttpPatch("{locationId:guid}/units/{unitId:guid}/activate")]
    [Authorize(Roles = "Admin,Provider")]
    public async Task<IActionResult> ActivateUnit(Guid locationId, Guid unitId)
    {
        var listing = await db.Listings
            .FirstOrDefaultAsync(l => l.Id == unitId && l.LocationId == locationId);

        if (listing is null) return NotFound(new { error = "Unit not found." });
        if (!await CanAccess(listing.SupplierId)) return Forbid();

        listing.IsActive  = true;
        listing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { id = listing.Id, isActive = listing.IsActive });
    }

    // ── PATCH /api/locations/{locationId}/units/{unitId}/deactivate ───────────
    /// <summary>Deactivates an individual unit without affecting the location's published state.</summary>
    [HttpPatch("{locationId:guid}/units/{unitId:guid}/deactivate")]
    [Authorize(Roles = "Admin,Provider")]
    public async Task<IActionResult> DeactivateUnit(Guid locationId, Guid unitId)
    {
        var listing = await db.Listings
            .FirstOrDefaultAsync(l => l.Id == unitId && l.LocationId == locationId);

        if (listing is null) return NotFound(new { error = "Unit not found." });
        if (!await CanAccess(listing.SupplierId)) return Forbid();

        listing.IsActive  = false;
        listing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { id = listing.Id, isActive = listing.IsActive });
    }

    // POST /api/locations/{id}/units/import  (legacy — single import without per-row validation)
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
            if (!Enum.TryParse<ListingType>(row.Type, true, out var lt)) continue;
            var listing = new Listing
            {
                Id            = Guid.NewGuid(),
                SupplierId    = location.SupplierId,
                LocationId    = id,
                Type          = lt,
                Title         = row.Title ?? string.Empty,
                Address       = location.Address,
                City          = location.City,
                Lat           = location.Lat,
                Lng           = location.Lng,
                PriceFrom     = row.PriceFrom,
                PriceUnit     = row.PriceUnit ?? "€/month",
                SizeM2        = row.SizeM2,
                QuantityTotal = row.QuantityTotal ?? 1,
                Description   = row.Description ?? "",
                VatRate       = row.VatRate,
                FeaturesJson  = row.Features is not null
                                    ? System.Text.Json.JsonSerializer.Serialize(row.Features)
                                    : "{}",
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

    // ── POST /api/admin/locations/{locationId}/units/bulk ──────────────────────
    /// <summary>
    /// Bulk-import up to 200 units into an existing location.
    /// Each row is validated independently — one bad row does NOT fail the batch.
    /// Returns per-row error messages for failed rows.
    /// </summary>
    [HttpPost("{locationId:guid}/units/bulk")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> BulkImportUnits(
        Guid locationId, [FromBody] List<ImportUnitRow> rows)
    {
        if (rows is null || rows.Count == 0)
            return BadRequest(Error("Request body must be a non-empty JSON array."));
        if (rows.Count > 200)
            return BadRequest(Error("Maximum 200 units per request."));

        var location = await db.SupplierLocations
            .Include(l => l.Supplier)
            .FirstOrDefaultAsync(l => l.Id == locationId);
        if (location is null)
            return NotFound(Error("Location not found."));

        return await ExecuteBulkUnitInsert(location, rows, User.GetUserEmail());
    }

    // ── POST /api/admin/locations/bulk ─────────────────────────────────────────
    /// <summary>
    /// Create a new supplier location and bulk-import its units in a single call.
    /// Returns the new locationId plus the same bulk-result shape as the units endpoint.
    /// </summary>
    [HttpPost("bulk")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> BulkCreateLocation([FromBody] BulkCreateLocationRequest body)
    {
        if (!body.SupplierId.HasValue || body.SupplierId.Value == Guid.Empty)
            return BadRequest(Error("supplierId is required."));
        if (string.IsNullOrWhiteSpace(body.Name))
            return BadRequest(Error("name is required."));

        var supplier = await db.Suppliers.FindAsync(body.SupplierId.Value);
        if (supplier is null)
            return NotFound(Error("Supplier not found."));

        var rows = body.Units ?? [];
        if (rows.Count > 200)
            return BadRequest(Error("Maximum 200 units per request."));

        var location = new SupplierLocation
        {
            Id          = Guid.NewGuid(),
            SupplierId  = supplier.Id,
            Name        = body.Name,
            Address     = body.Address,
            City        = body.City,
            Country     = supplier.Country ?? "EE",
            Lat         = body.Lat,
            Lng         = body.Lng,
            Description = string.Empty,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        };
        db.SupplierLocations.Add(location);
        await db.SaveChangesAsync();  // get the ID committed before inserting units

        location.Supplier = supplier;

        var result = await ExecuteBulkUnitInsert(location, rows, User.GetUserEmail(),
            extraAuditDetail: $"(via bulk location create)");

        // Wrap result JSON to add locationId
        if (result is OkObjectResult ok)
        {
            var inner = ok.Value!;
            // reflect the anonymous object from ExecuteBulkUnitInsert
            var t       = inner.GetType();
            var created = (int)t.GetProperty("created")!.GetValue(inner)!;
            var failed  = (int)t.GetProperty("failed")!.GetValue(inner)!;
            var errors  = (List<string>)t.GetProperty("errors")!.GetValue(inner)!;
            return Ok(new { locationId = location.Id, created, failed, errors });
        }
        return result;
    }

    // ── Shared bulk-insert helper ──────────────────────────────────────────────
    private async Task<IActionResult> ExecuteBulkUnitInsert(
        SupplierLocation location,
        List<ImportUnitRow> rows,
        string actorEmail,
        string? extraAuditDetail = null)
    {
        var errors  = new List<string>();
        var created = 0;

        for (var i = 0; i < rows.Count; i++)
        {
            var row     = rows[i];
            var rowNum  = i + 1;
            var rowErrors = new List<string>();

            if (string.IsNullOrWhiteSpace(row.Title))
                rowErrors.Add($"Row {rowNum}: title is required");
            if (string.IsNullOrWhiteSpace(row.Type) ||
                !Enum.TryParse<ListingType>(row.Type, true, out var listingType))
            {
                rowErrors.Add($"Row {rowNum}: type must be one of Warehouse, Moving, Trailer");
                listingType = ListingType.Warehouse; // placeholder — row will be skipped
            }
            if (row.PriceFrom <= 0)
                rowErrors.Add($"Row {rowNum}: priceFrom must be > 0");

            if (rowErrors.Count > 0)
            {
                errors.AddRange(rowErrors);
                continue;
            }

            try
            {
                var listing = new Listing
                {
                    Id            = Guid.NewGuid(),
                    SupplierId    = location.SupplierId,
                    LocationId    = location.Id,
                    Type          = listingType,
                    Title         = row.Title!,
                    Address       = location.Address,
                    City          = location.City,
                    Lat           = location.Lat,
                    Lng           = location.Lng,
                    PriceFrom     = row.PriceFrom,
                    PriceUnit     = row.PriceUnit ?? "€/month",
                    SizeM2        = row.SizeM2,
                    QuantityTotal = row.QuantityTotal ?? 1,
                    Description   = row.Description ?? string.Empty,
                    VatRate       = row.VatRate,
                    FeaturesJson  = row.Features is not null
                                        ? System.Text.Json.JsonSerializer.Serialize(row.Features)
                                        : "{}",
                    IsActive      = true,
                    AvailableNow  = true,
                    CreatedAt     = DateTime.UtcNow,
                    UpdatedAt     = DateTime.UtcNow,
                };
                db.Listings.Add(listing);
                await db.SaveChangesAsync();
                created++;
            }
            catch (Exception)
            {
                db.ChangeTracker.Clear();
                errors.Add($"Row {rowNum}: database error — unit not saved");
            }
        }

        // Audit log
        var detail = $"Bulk import: {created} units created in location {location.Id}" +
                     (extraAuditDetail is not null ? $" {extraAuditDetail}" : string.Empty);
        db.AuditLogs.Add(new AuditLog
        {
            Id        = Guid.NewGuid(),
            Action    = "listing.bulk_import",
            Actor     = actorEmail,
            Target    = $"Location:{location.Id}",
            Detail    = detail,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        return Ok(new { created, failed = errors.Count, errors });
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

    private async Task<Dictionary<Guid, int>> LoadBookedCountsAsync(SupplierLocation loc)
    {
        var listingIds = loc.Listings.Where(u => u.IsActive).Select(u => u.Id).ToList();
        if (listingIds.Count == 0) return [];
        var now = DateTime.UtcNow;
        return await db.Bookings
            .Where(b => listingIds.Contains(b.ListingId)
                     && (b.Status == BookingStatus.Confirmed
                         || b.Status == BookingStatus.Active
                         || b.Status == BookingStatus.Reserved)
                     && b.StartDate <= now
                     && (!b.EndDate.HasValue || b.EndDate.Value > now))
            .GroupBy(b => b.ListingId)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
    }

    private static (int available, bool fullyBooked) ComputeAvailability(
        SupplierLocation loc, Dictionary<Guid, int> bookedCounts)
    {
        var activeListings = loc.Listings.Where(u => u.IsActive).ToList();
        if (activeListings.Count == 0) return (0, false);

        var totalAvailable = 0;
        foreach (var listing in activeListings)
        {
            var capacity       = listing.QuantityTotal ?? 1;
            var activeBookings = bookedCounts.GetValueOrDefault(listing.Id, 0);
            totalAvailable    += Math.Max(0, capacity - activeBookings);
        }
        return (totalAvailable, totalAvailable == 0);
    }

    private static SupplierLocationDto MapToDto(SupplierLocation l, int availableUnits, bool fullyBooked)
    {
        var activeUnits  = l.Listings.Where(u => u.IsActive).ToList();
        var ratedUnits   = activeUnits.Where(u => u.ReviewCount > 0).ToList();
        var avgRating    = ratedUnits.Any()
            ? Math.Round(ratedUnits.Average(u => u.Rating), 1) : 0m;
        var totalReviews = activeUnits.Sum(u => u.ReviewCount);
        var bestDiscount = activeUnits
            .Select(u => u.ClientDiscountRateOverride ?? l.Supplier?.ClientDiscountRate ?? 0)
            .Where(d => d > 0)
            .DefaultIfEmpty(0)
            .Max();

        return new SupplierLocationDto(
            Id:             l.Id,
            SupplierId:     l.SupplierId,
            SupplierName:   l.Supplier?.Name ?? "",
            Name:           l.Name,
            Address:        l.Address,
            City:           l.City,
            Lat:            l.Lat,
            Lng:            l.Lng,
            Notes:          l.Notes,
            Images:         l.Images,
            Description:    l.Description,
            OpeningHours:   l.OpeningHours,
            UnitCount:      activeUnits.Count,
            AvailableUnits: availableUnits,
            FullyBooked:    fullyBooked,
            PriceFrom:      activeUnits.Select(u => (decimal?)u.PriceFrom).Min(),
            CreatedAt:      l.CreatedAt.ToString("yyyy-MM-dd"),
            Units:          activeUnits
                             .OrderBy(u => u.PriceFrom)
                             .Select(u => new ListingDto(
                                 u.Id, u.Type.ToString().ToLower(), u.Title,
                                 l.Supplier?.Name ?? "", l.Address, l.City,
                                 l.Lat, l.Lng, u.PriceFrom, u.PriceUnit, u.AvailableNow,
                                 u.Badge?.ToString().ToLower(), u.Rating, u.ReviewCount,
                                 u.Description, u.Images, u.Features,
                                 u.PartnerDiscountRateOverride, u.ClientDiscountRateOverride,
                                 l.Supplier?.ClientDiscountRate,
                                 null, null,  // EffectiveCustomerDiscount, EffectivePartnerDiscount — not computed in nested location view
                                 u.VatRate, u.PricesIncludeVat, u.SupplierId,
                                 u.SizeM2, u.QuantityTotal, u.LocationId, u.ViewCount,
                                 l.Supplier?.IsVerified ?? false,
                                 l.Supplier?.FoundingPartner ?? false))
                             .ToList(),
            Rating:               avgRating,
            ReviewCount:          totalReviews,
            BestCustomerDiscount: bestDiscount > 0 ? (decimal?)bestDiscount : null,
            ExternalId:           l.ExternalId
        );
    }
}
