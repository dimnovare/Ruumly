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

        // All aggregations pushed server-side — no ToList() on bookings
        var totalBookings     = await baseQuery.CountAsync();
        var thisMonthBookings = await baseQuery.CountAsync(b => b.CreatedAt >= monthStart);
        var thisMonthRevenue  = await baseQuery
            .Where(b => b.CreatedAt >= monthStart)
            .SumAsync(b => (decimal?)b.Total) ?? 0m;
        var activeBookings    = await baseQuery
            .CountAsync(b => b.Status == BookingStatus.Reserved
                          || b.Status == BookingStatus.Confirmed
                          || b.Status == BookingStatus.Active);
        var totalRevenue      = await baseQuery
            .Where(b => b.Status != BookingStatus.Cancelled)
            .SumAsync(b => (decimal?)b.Total) ?? 0m;
        var bookedListingIds  = await baseQuery
            .Where(b => b.Status == BookingStatus.Reserved
                     || b.Status == BookingStatus.Confirmed
                     || b.Status == BookingStatus.Active)
            .Select(b => b.ListingId)
            .Distinct()
            .CountAsync();

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
            var supplier = await db.Suppliers.FindAsync(effectiveId);
            hasFullAnalytics = supplier?.Tier >= SupplierTier.Standard;
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
