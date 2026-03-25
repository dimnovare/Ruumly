using Asp.Versioning;
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
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/reviews")]
public class ReviewsController(RuumlyDbContext db) : ControllerBase
{
    // ──────────────────────────────────────────────────────────────────────────
    // POST /api/reviews  — authenticated customers only
    // ──────────────────────────────────────────────────────────────────────────

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateReview([FromBody] CreateReviewRequest req)
    {
        // 1. Validate rating range
        if (req.Rating is < 1 or > 5)
            return BadRequest(new { error = "Rating must be between 1 and 5." });

        // 2. Validate comment length
        if (req.Comment is not null && req.Comment.Length > 1000)
            return BadRequest(new { error = "Comment must not exceed 1 000 characters." });

        var userId = User.GetUserId();

        // 3. Load booking and verify ownership + status
        var booking = await db.Bookings
            .Include(b => b.Listing)
            .FirstOrDefaultAsync(b => b.Id == req.BookingId);

        if (booking is null || booking.UserId != userId)
            return NotFound(new { error = "Booking not found." });

        if (booking.Status != BookingStatus.Completed)
            return BadRequest(new { error = "You can only review a completed booking." });

        // 4. Ensure no duplicate review for this booking
        var alreadyReviewed = await db.Reviews.AnyAsync(r => r.BookingId == req.BookingId);
        if (alreadyReviewed)
            return Conflict(new { error = "You have already reviewed this booking." });

        // 5. Persist review
        var review = new Review
        {
            Id         = Guid.NewGuid(),
            BookingId  = req.BookingId,
            UserId     = userId,
            ListingId  = booking.ListingId,
            SupplierId = booking.SupplierId,
            Rating     = req.Rating,
            Comment    = req.Comment?.Trim(),
            CreatedAt  = DateTime.UtcNow,
        };

        db.Reviews.Add(review);
        await db.SaveChangesAsync();

        // 6. Recalculate listing aggregate (weighted average)
        await RecalculateListingRatingAsync(booking.ListingId);

        // 7. Recalculate supplier aggregate
        await RecalculateSupplierRatingAsync(booking.SupplierId);

        await db.SaveChangesAsync();

        var user = await db.Users.FindAsync(userId);
        return CreatedAtAction(
            nameof(GetReviews),
            new { listingId = review.ListingId },
            MapToDto(review, user?.Name ?? "Anonymous"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GET /api/reviews?listingId={id}  or  ?supplierId={id}  — public
    // ──────────────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetReviews(
        [FromQuery] Guid? listingId,
        [FromQuery] Guid? supplierId)
    {
        if (listingId is null && supplierId is null)
            return BadRequest(new { error = "Provide listingId or supplierId." });

        var query = db.Reviews
            .Include(r => r.User)
            .AsQueryable();

        if (listingId.HasValue)
            query = query.Where(r => r.ListingId == listingId.Value);
        else
            query = query.Where(r => r.SupplierId == supplierId!.Value);

        var reviews = await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return Ok(reviews.Select(r => MapToDto(r, r.User?.Name ?? "Anonymous")));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private async Task RecalculateListingRatingAsync(Guid listingId)
    {
        var listing = await db.Listings.FindAsync(listingId);
        if (listing is null) return;

        var stats = await db.Reviews
            .Where(r => r.ListingId == listingId)
            .GroupBy(_ => 1)
            .Select(g => new { Avg = g.Average(r => (double)r.Rating), Count = g.Count() })
            .FirstOrDefaultAsync();

        listing.Rating      = stats is null ? 0m : Math.Round((decimal)stats.Avg, 2);
        listing.ReviewCount = stats?.Count ?? 0;
    }

    private async Task RecalculateSupplierRatingAsync(Guid supplierId)
    {
        var supplier = await db.Suppliers.FindAsync(supplierId);
        if (supplier is null) return;

        var stats = await db.Reviews
            .Where(r => r.SupplierId == supplierId)
            .GroupBy(_ => 1)
            .Select(g => new { Avg = g.Average(r => (double)r.Rating), Count = g.Count() })
            .FirstOrDefaultAsync();

        supplier.Rating      = stats is null ? 0m : Math.Round((decimal)stats.Avg, 2);
        supplier.ReviewCount = stats?.Count ?? 0;
    }

    private static ReviewDto MapToDto(Review r, string userName) => new(
        Id:        r.Id,
        Rating:    r.Rating,
        Comment:   r.Comment,
        UserName:  userName,
        CreatedAt: r.CreatedAt.ToString("yyyy-MM-dd"));
}
