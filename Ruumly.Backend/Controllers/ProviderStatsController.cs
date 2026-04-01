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
        Guid effectiveId;
        if (supplierId.HasValue && User.GetUserRole() == UserRole.Admin)
            effectiveId = supplierId.Value;
        else
        {
            var user = await db.Users.FindAsync(User.GetUserId());
            if (user?.SupplierId is null) return BadRequest(new { error = "No supplier linked." });
            effectiveId = user.SupplierId.Value;
        }

        var now        = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var allBookings = await db.Bookings
            .Where(b => b.SupplierId == effectiveId && !b.IsDeleted)
            .ToListAsync();

        var thisMonth = allBookings.Where(b => b.CreatedAt >= monthStart).ToList();
        var active    = allBookings.Where(b =>
            b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Active).ToList();

        var totalUnits = await db.Listings
            .CountAsync(l => l.SupplierId == effectiveId && l.IsActive);

        return Ok(new
        {
            totalBookings     = allBookings.Count,
            thisMonthBookings = thisMonth.Count,
            thisMonthRevenue  = thisMonth.Sum(b => b.Total),
            activeBookings    = active.Count,
            totalUnits,
            totalRevenue      = allBookings
                .Where(b => b.Status != BookingStatus.Cancelled)
                .Sum(b => b.Total),
        });
    }
}
