using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Constants;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/listings")]
public class ListingsController(IListingService listingService, RuumlyDbContext db) : ControllerBase
{
    /// <summary>
    /// Search listings with optional filters.
    /// Public — no auth required.
    /// </summary>
    [HttpGet]
    [EnableRateLimiting("search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] ListingSearchRequest filters,
        [FromQuery(Name = "lang")] string? language = null)
    {
        language ??= ResolveLanguageFromHeader();
        var result = await listingService.SearchAsync(filters, language);
        return Ok(result);
    }

    /// <summary>
    /// Returns up to 4 featured (badged) listings.
    /// Public — no auth required.
    /// </summary>
    [HttpGet("featured")]
    [EnableRateLimiting("search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Featured([FromQuery(Name = "lang")] string? language = null)
    {
        language ??= ResolveLanguageFromHeader();
        var result = await listingService.GetFeaturedAsync(language);
        return Ok(result);
    }

    [HttpGet("size-buckets")]
    [AllowAnonymous]
    [ResponseCache(Duration = 3600)]
    public IActionResult GetSizeBuckets()
    {
        var buckets = StorageSizeBuckets.All.Select(b => new
        {
            code  = b.Code,
            minM2 = b.MinM2,
            maxM2 = b.MaxM2,
        });
        return Ok(buckets);
    }

    /// <summary>
    /// Returns a single listing by ID.
    /// Public — no auth required.
    /// </summary>
    [HttpGet("{id:guid}")]
    [EnableRateLimiting("search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, [FromQuery(Name = "lang")] string? language = null)
    {
        language ??= ResolveLanguageFromHeader();
        var listing = await listingService.GetByIdAsync(id, language);
        if (listing is null) return NotFound(new { error = "Not Found", message = "Listing not found", statusCode = 404 });
        return Ok(listing);
    }

    /// <summary>
    /// Check unit availability for a listing in a given date range.
    /// Returns available capacity without creating a booking.
    /// </summary>
    [HttpGet("{id:guid}/availability")]
    [AllowAnonymous]
    public async Task<IActionResult> CheckAvailability(
        Guid id,
        [FromQuery] string startDate,
        [FromQuery] string endDate)
    {
        if (!DateTime.TryParse(startDate, out var start) ||
            !DateTime.TryParse(endDate, out var end))
            return BadRequest(new { error = "Invalid date format" });

        var startUtc = DateTime.SpecifyKind(start, DateTimeKind.Utc);
        var endUtc   = DateTime.SpecifyKind(end,   DateTimeKind.Utc);

        var listing = await db.Listings
            .FirstOrDefaultAsync(l => l.Id == id && l.IsActive);
        if (listing is null)
            return NotFound();

        var totalUnits = listing.QuantityTotal ?? 1;

        var bookedCount = await db.Bookings
            .CountAsync(b =>
                b.ListingId == id &&
                (b.Status == BookingStatus.Confirmed ||
                 b.Status == BookingStatus.Active    ||
                 b.Status == BookingStatus.Reserved) &&
                b.EndDate.HasValue &&
                b.StartDate < endUtc &&
                b.EndDate.Value > startUtc);

        var available = totalUnits - bookedCount;

        return Ok(new
        {
            listingId   = id,
            totalUnits,
            bookedCount,
            available   = Math.Max(0, available),
            isAvailable = available > 0,
        });
    }

    // Falls back to the first Accept-Language tag (e.g. "et-EE" → "et") when the
    // ?lang= query parameter is not supplied. Returns null if neither is present.
    private string? ResolveLanguageFromHeader()
    {
        if (!Request.Headers.TryGetValue("Accept-Language", out var al)) return null;
        var first = al.ToString().Split(',')[0].Split('-')[0].Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(first) ? null : first;
    }
}
