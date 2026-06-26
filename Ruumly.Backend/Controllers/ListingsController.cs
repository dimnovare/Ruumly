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
        var result = await listingService.SearchAsync(filters, language, HttpContext.RequestAborted);
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
                ((b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.AwaitingConfirmation) ||
                 b.Status == BookingStatus.Active    ||
                 b.Status == BookingStatus.Reserved) &&
                b.StartDate < endUtc &&
                // Open-ended (null EndDate) bookings are ongoing month-to-month storage
                // rentals — they still occupy a unit. Mirror the booking-overlap guard so
                // this public count agrees with the booking outcome (was excluding them,
                // over-reporting availability and then rejecting the booking).
                (b.EndDate == null || b.EndDate.Value > startUtc));

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

    /// <summary>
    /// Public, read-only calendar feed of UNAVAILABLE days for a single listing.
    /// Powers the customer-facing availability calendar on the detail page.
    ///
    /// Returns ONLY date strings (yyyy-MM-dd). No customer PII, no booking ids,
    /// no prices, no names — just which days the unit cannot be booked, derived from:
    ///   1. Provider blackout dates that block the whole parent location (ListingId == null),
    ///   2. Provider blackout dates scoped to this specific listing (ListingId == this id),
    ///   3. Days on which every unit of this listing is already taken by an active
    ///      (non-cancelled, non-completed) booking — i.e. fully-booked days.
    ///
    /// The window defaults to today..+120 days and is capped at 365 days so the
    /// response stays small and cache-friendly. Empty DB → empty "blocked" array
    /// (the unit reads as fully available).
    /// </summary>
    [HttpGet("{id:guid}/blocked-dates")]
    [AllowAnonymous]
    [EnableRateLimiting("search")]
    [ResponseCache(Duration = 300)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPublicBlockedDates(
        Guid id,
        [FromQuery] string? from = null,
        [FromQuery] string? to   = null)
    {
        var listing = await db.Listings
            .Where(l => l.Id == id && l.IsActive)
            .Select(l => new { l.Id, l.LocationId, l.QuantityTotal })
            .FirstOrDefaultAsync();
        if (listing is null)
            return NotFound();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var windowStart = DateOnly.TryParse(from, out var f) && f >= today ? f : today;
        var windowEnd   = DateOnly.TryParse(to,   out var t) ? t : windowStart.AddDays(120);
        // Clamp the window to at most 365 days to keep the payload bounded.
        if (windowEnd <= windowStart) windowEnd = windowStart.AddDays(120);
        if (windowEnd > windowStart.AddDays(365)) windowEnd = windowStart.AddDays(365);

        var blocked = new HashSet<DateOnly>();

        // 1 + 2. Provider blackout dates — whole-location blocks and listing-scoped
        //        blocks both make this unit unbookable that day.
        var blackoutQuery = db.BlockedDates
            .Where(b => b.Date >= windowStart && b.Date <= windowEnd)
            .Where(b =>
                (listing.LocationId != null && b.LocationId == listing.LocationId && b.ListingId == null)
                || b.ListingId == id);
        foreach (var d in await blackoutQuery.Select(b => b.Date).ToListAsync())
            blocked.Add(d);

        // 3. Fully-booked days. Pull active bookings that overlap the window and
        //    mark each day where occupancy reaches the unit's total capacity.
        var capacity = listing.QuantityTotal ?? 1;
        var windowStartUtc = DateTime.SpecifyKind(windowStart.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var windowEndUtc   = DateTime.SpecifyKind(windowEnd.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var bookingRanges = await db.Bookings
            .Where(b => b.ListingId == id
                     && ((b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.AwaitingConfirmation)
                         || b.Status == BookingStatus.Active
                         || b.Status == BookingStatus.Reserved)
                     && b.StartDate < windowEndUtc
                     && (b.EndDate == null || b.EndDate.Value > windowStartUtc))
            .Select(b => new { b.StartDate, b.EndDate })
            .ToListAsync();

        if (bookingRanges.Count > 0)
        {
            // Per-day occupancy across the window; a day is "blocked" only when every
            // unit is taken. Open-ended (null EndDate) bookings run to the window end.
            var occupancy = new Dictionary<DateOnly, int>();
            foreach (var r in bookingRanges)
            {
                var rangeStart = DateOnly.FromDateTime(r.StartDate);
                if (rangeStart < windowStart) rangeStart = windowStart;
                var rangeEnd = r.EndDate.HasValue
                    ? DateOnly.FromDateTime(r.EndDate.Value).AddDays(-1) // EndDate is exclusive checkout day
                    : windowEnd;
                if (rangeEnd > windowEnd) rangeEnd = windowEnd;
                for (var d = rangeStart; d <= rangeEnd; d = d.AddDays(1))
                    occupancy[d] = occupancy.GetValueOrDefault(d) + 1;
            }
            foreach (var (day, count) in occupancy)
                if (count >= capacity) blocked.Add(day);
        }

        var dates = blocked
            .Where(d => d >= windowStart && d <= windowEnd)
            .OrderBy(d => d)
            .Select(d => d.ToString("yyyy-MM-dd"))
            .ToList();

        return Ok(new
        {
            listingId   = id,
            from        = windowStart.ToString("yyyy-MM-dd"),
            to          = windowEnd.ToString("yyyy-MM-dd"),
            blockedDates = dates,
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
