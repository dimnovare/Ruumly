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
        bool aggregateAllSuppliers = false;
        Guid effectiveId = Guid.Empty;

        if (User.GetUserRole() == UserRole.Admin)
        {
            if (supplierId.HasValue) effectiveId = supplierId.Value;
            else aggregateAllSuppliers = true;
        }
        else
        {
            var user = await db.Users.FindAsync(User.GetUserId());
            if (user?.SupplierId is null)
                return BadRequest(new { error = "No supplier linked." });
            effectiveId = user.SupplierId.Value;
        }

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
                                              || b.Status == BookingStatus.Confirmed
                                              || b.Status == BookingStatus.Active),
                TotalRevenue      = (decimal?)g.Where(b => b.Status != BookingStatus.Cancelled)
                                               .Sum(b => b.Total),
                BookedListingCount = g.Where(b => b.Status == BookingStatus.Reserved
                                               || b.Status == BookingStatus.Confirmed
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

        bool hasFullAnalytics;
        if (aggregateAllSuppliers)
        {
            hasFullAnalytics = true;
        }
        else
        {
            var tier = await db.Suppliers
                .Where(s => s.Id == effectiveId)
                .Select(s => s.Tier)
                .FirstOrDefaultAsync();
            hasFullAnalytics = tier >= SupplierTier.Standard;
        }

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
}
