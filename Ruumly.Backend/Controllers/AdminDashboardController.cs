using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Controllers;

[Route("api/admin/dashboard")]
public class AdminDashboardController(RuumlyDbContext db) : AdminBaseController(db)
{
    // ── GET /api/admin/dashboard/stats ─────────────────────────────────────────
    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var now           = DateTime.UtcNow;
        var weekStart     = now.Date.AddDays(-(int)now.DayOfWeek);   // Sunday 00:00 UTC
        var monthStart    = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // Revenue = sum of Total on non-cancelled bookings
        var revenueThisWeek = await Db.Bookings
            .Where(b => b.CreatedAt >= weekStart &&
                        b.Status != BookingStatus.Cancelled)
            .SumAsync(b => (decimal?)b.Total) ?? 0m;

        var revenueThisMonth = await Db.Bookings
            .Where(b => b.CreatedAt >= monthStart &&
                        b.Status != BookingStatus.Cancelled)
            .SumAsync(b => (decimal?)b.Total) ?? 0m;

        var bookingsThisWeek = await Db.Bookings
            .CountAsync(b => b.CreatedAt >= weekStart &&
                             b.Status != BookingStatus.Cancelled);

        var bookingsThisMonth = await Db.Bookings
            .CountAsync(b => b.CreatedAt >= monthStart &&
                             b.Status != BookingStatus.Cancelled);

        var newSuppliersThisMonth = await Db.Suppliers
            .CountAsync(s => s.CreatedAt >= monthStart);

        return Ok(new
        {
            revenueThisWeek,
            revenueThisMonth,
            bookingsThisWeek,
            bookingsThisMonth,
            newSuppliersThisMonth,
            conversionRate = (decimal?)null,   // no listing-view tracking yet
        });
    }

    // ── GET /api/admin/dashboard/revenue ──────────────────────────────────────
    [HttpGet("revenue")]
    public async Task<IActionResult> GetRevenue(
        [FromQuery] string? period     = "month",
        [FromQuery] Guid?   supplierId = null)
    {
        var now         = DateTime.UtcNow;
        var periodStart = period switch
        {
            "week" => now.AddDays(-7),
            "year" => now.AddYears(-1),
            _      => new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        var query = Db.Bookings
            .Include(b => b.Supplier)
            .Where(b => !b.IsDeleted && b.CreatedAt >= periodStart);

        if (supplierId.HasValue)
            query = query.Where(b => b.SupplierId == supplierId.Value);

        var bookings = await query.ToListAsync();

        var supplierBreakdown = bookings
            .GroupBy(b => new { b.SupplierId, b.Supplier.Name })
            .Select(g => new
            {
                supplierId       = g.Key.SupplierId,
                supplierName     = g.Key.Name,
                bookingCount     = g.Count(),
                gmv              = g.Sum(b => b.Total),
                customerPaid     = g.Sum(b => b.PlatformPrice + b.ExtrasTotal),
                avgMarginPercent = g.Average(b => b.BasePrice > 0
                    ? (b.PlatformPrice - b.BasePrice * 0.85m) / b.BasePrice * 100
                    : 0),
            })
            .OrderByDescending(x => x.gmv)
            .ToList();

        var subscriptionRevenue = await Db.Suppliers
            .Where(s => s.IsActive)
            .SumAsync(s => s.MonthlyFee);

        return Ok(new
        {
            period,
            totalBookings    = bookings.Count,
            totalGmv         = bookings.Sum(b => b.Total),
            subscriptionMrr  = subscriptionRevenue,
            supplierBreakdown,
        });
    }
}
