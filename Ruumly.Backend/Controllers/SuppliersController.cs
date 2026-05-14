using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/suppliers")]
public class SuppliersController(
    IFeaturedPartnersService featuredPartnersService,
    ISupplierProfileService  supplierProfileService,
    IPlacesService           placesService,
    RuumlyDbContext          db) : ControllerBase
{
    /// <summary>
    /// Returns up to 6 featured partners (verified Standard/Premium suppliers),
    /// rotating daily. Public — no auth required.
    /// </summary>
    [HttpGet("featured")]
    [EnableRateLimiting("search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFeatured()
    {
        var result = await featuredPartnersService.GetFeaturedAsync();
        return Ok(result);
    }

    /// <summary>
    /// Returns the public profile for a partner identified by slug.
    /// Public — no auth required.
    /// </summary>
    [HttpGet("by-slug/{slug}")]
    [EnableRateLimiting("search")]
    [ProducesResponseType(typeof(SupplierProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct)
    {
        // Slug shape guard — reject anything not [a-z0-9-]{2,80} before any DB or cache work.
        if (string.IsNullOrWhiteSpace(slug) ||
            slug.Length is < 2 or > 80 ||
            !Regex.IsMatch(slug, "^[a-z0-9-]+$"))
        {
            return NotFound();
        }

        var profile = await supplierProfileService.GetBySlugAsync(slug, ct);
        return profile is null ? NotFound() : Ok(profile);
    }

    /// <summary>
    /// Returns Google Places rating + up to 5 reviews for the partner identified by slug.
    /// Returns 204 when no Google Place ID is configured or the feature is disabled.
    /// Public — no auth required.
    /// </summary>
    [HttpGet("by-slug/{slug}/google-reviews")]
    [EnableRateLimiting("search")]
    [ProducesResponseType(typeof(GooglePlaceSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> GetGoogleReviews(
        string slug,
        [FromQuery] string lang = "et",
        CancellationToken ct = default)
    {
        if (!Regex.IsMatch(slug, "^[a-z0-9-]{2,80}$")) return NoContent();

        var supplier = await db.Suppliers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Slug == slug && s.IsActive, ct);

        if (supplier?.GooglePlaceId is null) return NoContent();

        var reviews = await placesService.GetReviewsAsync(supplier.GooglePlaceId, lang, ct);
        return reviews is null ? NoContent() : Ok(reviews);
    }
}
