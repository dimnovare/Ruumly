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

        var bookingsQuery = db.Bookings.Where(b => !b.IsDeleted);
        if (!aggregateAllSuppliers)
            bookingsQuery = bookingsQuery.Where(b => b.SupplierId == effectiveId);
        var allBookings = await bookingsQuery.ToListAsync();

        var thisMonth = allBookings.Where(b => b.CreatedAt >= monthStart).ToList();
        var active    = allBookings.Where(b =>
            b.Status is BookingStatus.Reserved or BookingStatus.Confirmed or BookingStatus.Active).ToList();

        var listingsQuery = db.Listings.Where(l => l.IsActive);
        if (!aggregateAllSuppliers)
            listingsQuery = listingsQuery.Where(l => l.SupplierId == effectiveId);
        var totalUnits = await listingsQuery.CountAsync();

        var bookedUnits   = active.Select(b => b.ListingId).Distinct().Count();
        var occupancyRate = totalUnits > 0
            ? Math.Round((decimal)bookedUnits / totalUnits * 100, 1)
            : 0m;

        bool hasFullAnalytics;
        if (aggregateAllSuppliers)
        {
            // Admin viewing the aggregate always sees full analytics.
            hasFullAnalytics = true;
        }
        else
        {
            var supplier = await db.Suppliers.FindAsync(effectiveId);
            hasFullAnalytics = supplier?.Tier >= SupplierTier.Standard;
        }

        return Ok(new
        {
            totalBookings     = allBookings.Count,
            thisMonthBookings = thisMonth.Count,
            thisMonthRevenue  = thisMonth.Sum(b => b.Total),
            activeBookings    = active.Count,
            totalUnits,
            bookedUnits,
            occupancyRate,
            totalRevenue      = allBookings
                .Where(b => b.Status != BookingStatus.Cancelled)
                .Sum(b => b.Total),
            hasFullAnalytics,
        });
    }
}
