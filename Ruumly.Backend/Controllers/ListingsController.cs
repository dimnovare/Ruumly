using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ruumly.Backend.Constants;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/listings")]
public class ListingsController(IListingService listingService) : ControllerBase
{
    /// <summary>
    /// Search listings with optional filters.
    /// Public — no auth required.
    /// </summary>
    [HttpGet]
    [EnableRateLimiting("search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Search([FromQuery] ListingSearchRequest filters)
    {
        var result = await listingService.SearchAsync(filters);
        return Ok(result);
    }

    /// <summary>
    /// Returns up to 4 featured (badged) listings.
    /// Public — no auth required.
    /// </summary>
    [HttpGet("featured")]
    [EnableRateLimiting("search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Featured()
    {
        var result = await listingService.GetFeaturedAsync();
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
    public async Task<IActionResult> GetById(Guid id)
    {
        var listing = await listingService.GetByIdAsync(id);
        if (listing is null) return NotFound(new { error = "Not Found", message = "Listing not found", statusCode = 404 });
        return Ok(listing);
    }
}
