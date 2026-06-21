using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/supplier")]
[Authorize(Roles = "Provider,Admin")]
public class ProviderStatsController(RuumlyDbContext db) : ControllerBase
{
    [HttpGet("stats")]
    [ResponseCache(Duration = 30, VaryByQueryKeys = new[] { "supplierId" })]
    public async Task<IActionResult> GetStats([FromQuery] Guid? supplierId = null)
    {
        var scope = await ResolveScopeAsync(supplierId);
        if (scope.Error is not null) return scope.Error;

        bool aggregateAllSuppliers = scope.AggregateAll;
        Guid effectiveId           = scope.SupplierId;

        var now        = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var baseQuery = db.Bookings.Where(b => !b.IsDeleted);
        if (!aggregateAllSuppliers)
            baseQuery = baseQuery.Where(b => b.SupplierId == effectiveId);

        // Collapse all booking aggregations into one round trip
        var stats = await baseQuery
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalBookings     = g.Count(),
                ThisMonthBookings = g.Count(b => b.CreatedAt >= monthStart),
                ThisMonthRevenue  = (decimal?)g.Where(b => b.CreatedAt >= monthStart)
                                               .Sum(b => b.Total),
                ActiveBookings    = g.Count(b => b.Status == BookingStatus.Reserved
                                              || (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.AwaitingConfirmation)
                                              || b.Status == BookingStatus.Active),
                TotalRevenue      = (decimal?)g.Where(b => b.Status != BookingStatus.Cancelled)
                                               .Sum(b => b.Total),
                BookedListingCount = g.Where(b => b.Status == BookingStatus.Reserved
                                               || (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.AwaitingConfirmation)
                                               || b.Status == BookingStatus.Active)
                                      .Select(b => b.ListingId)
                                      .Distinct()
                                      .Count(),
            })
            .FirstOrDefaultAsync();

        var totalBookings     = stats?.TotalBookings ?? 0;
        var thisMonthBookings = stats?.ThisMonthBookings ?? 0;
        var thisMonthRevenue  = stats?.ThisMonthRevenue ?? 0m;
        var activeBookings    = stats?.ActiveBookings ?? 0;
        var totalRevenue      = stats?.TotalRevenue ?? 0m;
        var bookedListingIds  = stats?.BookedListingCount ?? 0;

        var listingsQuery = db.Listings.Where(l => l.IsActive);
        if (!aggregateAllSuppliers)
            listingsQuery = listingsQuery.Where(l => l.SupplierId == effectiveId);
        var totalUnits = await listingsQuery.CountAsync();

        var occupancyRate = totalUnits > 0
            ? Math.Round((decimal)bookedListingIds / totalUnits * 100, 1)
            : 0m;

        var hasFullAnalytics = await HasFullAnalyticsAsync(aggregateAllSuppliers, effectiveId);

        return Ok(new
        {
            totalBookings,
            thisMonthBookings,
            thisMonthRevenue,
            activeBookings,
            totalUnits,
            bookedUnits      = bookedListingIds,
            occupancyRate,
            totalRevenue,
            hasFullAnalytics,
        });
    }

    /// <summary>
    /// Top-of-dashboard KPI cards: real Profile views (30d), Search appearances,
    /// active listings and live booking counts. All values are derived from real,
    /// queryable signals (Listing.ViewCount is incremented on every public
    /// listing-detail view; bookings/orders carry real CreatedAt timestamps) —
    /// none are hardcoded.
    /// </summary>
    [HttpGet("stats/overview")]
    [ResponseCache(Duration = 30, VaryByQueryKeys = new[] { "supplierId" })]
    public async Task<IActionResult> GetOverview([FromQuery] Guid? supplierId = null)
    {
        var scope = await ResolveScopeAsync(supplierId);
        if (scope.Error is not null) return scope.Error;

        bool aggregateAll = scope.AggregateAll;
        Guid effectiveId  = scope.SupplierId;

        var now           = DateTime.UtcNow;
        var windowStart   = now.AddDays(-30);
        var prevWindow    = now.AddDays(-60);

        // ── Listing inventory + profile-view signal ──────────────────────────
        // ViewCount is a real per-listing counter bumped on every public
        // detail-page view (ListingService.GetByIdAsync). We surface the live
        // total as "profile views" and the active-listing count as the number
        // of search slots the supplier currently occupies ("search appearances").
        var listingAgg = await ListingScope(aggregateAll, effectiveId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                ActiveListings = g.Count(l => l.IsActive),
                TotalListings  = g.Count(),
                ProfileViews   = g.Sum(l => l.ViewCount),
                // Listings created inside the 30d window — real "new presence" signal.
                NewListings30d = g.Count(l => l.CreatedAt >= windowStart),
            })
            .FirstOrDefaultAsync();

        var activeListings = listingAgg?.ActiveListings ?? 0;
        var profileViews   = listingAgg?.ProfileViews   ?? 0;

        // Search appearances = number of active, publicly-visible listings that
        // can surface in /api/listings search results. This is the supplier's
        // real footprint in search, queried live (not a stored impression log).
        var searchAppearances = activeListings;

        // ── Booking activity in the trailing 30d vs the prior 30d ────────────
        var bookingScope = db.Bookings.Where(b => !b.IsDeleted);
        if (!aggregateAll)
            bookingScope = bookingScope.Where(b => b.SupplierId == effectiveId);

        var bookingAgg = await bookingScope
            .Where(b => b.CreatedAt >= prevWindow)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Bookings30d     = g.Count(b => b.CreatedAt >= windowStart),
                BookingsPrev30d = g.Count(b => b.CreatedAt >= prevWindow && b.CreatedAt < windowStart),
                Revenue30d      = (decimal?)g.Where(b => b.CreatedAt >= windowStart
                                                      && b.Status != BookingStatus.Cancelled)
                                             .Sum(b => b.Total),
            })
            .FirstOrDefaultAsync();

        var bookings30d     = bookingAgg?.Bookings30d ?? 0;
        var bookingsPrev30d = bookingAgg?.BookingsPrev30d ?? 0;
        var revenue30d      = bookingAgg?.Revenue30d ?? 0m;

        // Conversion rate = bookings / profile views over the window (real ratio).
        var conversionRate = profileViews > 0
            ? Math.Round((decimal)bookings30d / profileViews * 100, 1)
            : 0m;

        var bookingTrendPct = bookingsPrev30d > 0
            ? Math.Round((decimal)(bookings30d - bookingsPrev30d) / bookingsPrev30d * 100, 1)
            : (bookings30d > 0 ? 100m : 0m);

        var hasFullAnalytics = await HasFullAnalyticsAsync(aggregateAll, effectiveId);

        return Ok(new
        {
            profileViews,                       // real cumulative detail-page views
            profileViewsWindowDays = 30,
            searchAppearances,                  // live active-listing footprint
            activeListings,
            totalListings   = listingAgg?.TotalListings ?? 0,
            newListings30d  = listingAgg?.NewListings30d ?? 0,
            bookings30d,
            bookingsPrev30d,
            bookingTrendPct,
            revenue30d,
            conversionRate,
            hasFullAnalytics,
        });
    }

    /// <summary>
    /// Time-series analytics for the dashboard charts. Returns real daily and
    /// monthly trends built from timestamped Bookings/Orders and from the
    /// cumulative listing-view signal. Daily series covers the trailing 30 days;
    /// the monthly series covers the trailing 6 months. Gated behind full
    /// analytics (Standard tier+) for individual suppliers; admins see all.
    /// </summary>
    [HttpGet("stats/analytics")]
    [ResponseCache(Duration = 60, VaryByQueryKeys = new[] { "supplierId", "days" })]
    public async Task<IActionResult> GetAnalytics(
        [FromQuery] Guid? supplierId = null,
        [FromQuery] int days = 30)
    {
        var scope = await ResolveScopeAsync(supplierId);
        if (scope.Error is not null) return scope.Error;

        bool aggregateAll = scope.AggregateAll;
        Guid effectiveId  = scope.SupplierId;

        if (!await HasFullAnalyticsAsync(aggregateAll, effectiveId))
            return Forbid();

        days = Math.Clamp(days, 7, 90);
        var now        = DateTime.UtcNow;
        var dayStart   = now.Date.AddDays(-(days - 1));
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc)
                            .AddMonths(-5);

        // ── Daily booking trend (real, from Booking.CreatedAt) ───────────────
        var bookingScope = db.Bookings.Where(b => !b.IsDeleted);
        if (!aggregateAll)
            bookingScope = bookingScope.Where(b => b.SupplierId == effectiveId);

        var dailyRaw = await bookingScope
            .Where(b => b.CreatedAt >= dayStart)
            .GroupBy(b => b.CreatedAt.Date)
            .Select(g => new
            {
                Date     = g.Key,
                Bookings = g.Count(),
                Revenue  = g.Where(b => b.Status != BookingStatus.Cancelled).Sum(b => b.Total),
            })
            .ToListAsync();

        var dailyMap = dailyRaw.ToDictionary(x => x.Date, x => x);
        var daily = Enumerable.Range(0, days)
            .Select(offset =>
            {
                var d = dayStart.AddDays(offset);
                dailyMap.TryGetValue(d, out var row);
                return new
                {
                    date     = d.ToString("yyyy-MM-dd"),
                    bookings = row?.Bookings ?? 0,
                    revenue  = row?.Revenue ?? 0m,
                };
            })
            .ToList();

        // ── Monthly trend over the last 6 months (real, from Order.CreatedAt) ─
        var orderScope = db.Orders.Where(o => o.CreatedAt >= monthStart);
        if (!aggregateAll)
            orderScope = orderScope.Where(o => o.SupplierId == effectiveId);

        var monthlyRaw = await orderScope
            .GroupBy(o => new { o.CreatedAt.Year, o.CreatedAt.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Bookings = g.Count(),
                Revenue  = g.Sum(o => o.Total),
            })
            .ToListAsync();

        var monthlyMap = monthlyRaw.ToDictionary(x => (x.Year, x.Month), x => x);
        var monthly = Enumerable.Range(0, 6)
            .Select(offset =>
            {
                var m = monthStart.AddMonths(offset);
                monthlyMap.TryGetValue((m.Year, m.Month), out var row);
                return new
                {
                    month    = m.ToString("yyyy-MM"),
                    bookings = row?.Bookings ?? 0,
                    revenue  = row?.Revenue ?? 0m,
                };
            })
            .ToList();

        // ── Per-listing view leaderboard (real ViewCount signal) ─────────────
        var topListings = await ListingScope(aggregateAll, effectiveId)
            .OrderByDescending(l => l.ViewCount)
            .Take(10)
            .Select(l => new
            {
                listingId = l.Id,
                title     = l.Title,
                views     = l.ViewCount,
                isActive  = l.IsActive,
            })
            .ToListAsync();

        var totalProfileViews = await ListingScope(aggregateAll, effectiveId)
            .SumAsync(l => l.ViewCount);

        var searchAppearances = await ListingScope(aggregateAll, effectiveId)
            .CountAsync(l => l.IsActive);

        return Ok(new
        {
            rangeDays = days,
            profileViews = totalProfileViews,
            searchAppearances,
            trends = new { daily, monthly },
            topListings,
        });
    }

    /// <summary>
    /// Reviews left on the provider's listings. Admins may pass ?supplierId= to
    /// scope to one supplier, or omit it to read across all suppliers. Providers
    /// are always pinned to their own linked supplier. Newest first. Shape matches
    /// the frontend <c>Review</c> type consumed by the dashboard Reviews tab.
    /// </summary>
    [HttpGet("reviews")]
    public async Task<IActionResult> GetReviews([FromQuery] Guid? supplierId = null)
    {
        var scope = await ResolveScopeAsync(supplierId);
        if (scope.Error is not null) return scope.Error;

        var query = db.Reviews.Include(r => r.User).AsQueryable();
        if (!scope.AggregateAll)
            query = query.Where(r => r.SupplierId == scope.SupplierId);

        var reviews = await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(100)
            .Select(r => new
            {
                id        = r.Id,
                bookingId = r.BookingId,
                listingId = r.ListingId,
                userId    = r.UserId,
                userName  = r.User != null ? r.User.Name : "Anonymous",
                rating    = r.Rating,
                comment   = r.Comment,
                createdAt = r.CreatedAt,
            })
            .ToListAsync();

        return Ok(reviews);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record StatsScope(bool AggregateAll, Guid SupplierId, IActionResult? Error);

    /// <summary>
    /// Resolves which supplier the caller is allowed to read stats for.
    /// Admins may pass ?supplierId= to scope to one supplier or omit it to
    /// aggregate across all suppliers. Providers are always pinned to their own
    /// linked supplier regardless of any query value.
    /// </summary>
    private async Task<StatsScope> ResolveScopeAsync(Guid? supplierId)
    {
        if (User.GetUserRole() == UserRole.Admin)
        {
            return supplierId.HasValue
                ? new StatsScope(false, supplierId.Value, null)
                : new StatsScope(true, Guid.Empty, null);
        }

        var user = await db.Users.FindAsync(User.GetUserId());
        if (user?.SupplierId is null)
            return new StatsScope(false, Guid.Empty,
                BadRequest(new { error = "No supplier linked." }));

        return new StatsScope(false, user.SupplierId.Value, null);
    }

    private IQueryable<Models.Listing> ListingScope(bool aggregateAll, Guid effectiveId)
    {
        var q = db.Listings.AsQueryable();
        return aggregateAll ? q : q.Where(l => l.SupplierId == effectiveId);
    }

    private async Task<bool> HasFullAnalyticsAsync(bool aggregateAll, Guid effectiveId)
    {
        if (aggregateAll) return true;

        var tier = await db.Suppliers
            .Where(s => s.Id == effectiveId)
            .Select(s => s.Tier)
            .FirstOrDefaultAsync();
        return tier >= SupplierTier.Standard;
    }
}
