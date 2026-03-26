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
}
