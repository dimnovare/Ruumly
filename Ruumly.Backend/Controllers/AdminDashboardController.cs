using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Controllers;

[Route("api/admin/dashboard")]
public class AdminDashboardController(RuumlyDbContext db) : AdminBaseController(db)
{
    // ── GET /api/admin/dashboard/stats ─────────────────────────────────────────
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var totalListings      = await Db.Listings.CountAsync(l => l.IsActive);
        var totalOrders        = await Db.Orders.CountAsync();
        var totalUsers         = await Db.Users.CountAsync(u => u.Role != UserRole.Admin);
        var totalRevenue       = await Db.Invoices
                                    .Where(i => i.Status == InvoiceStatus.Paid)
                                    .SumAsync(i => (decimal?)i.Amount) ?? 0m;
        var ordersThisMonth    = await Db.Orders
                                    .CountAsync(o => o.CreatedAt >= monthStart);
        var revenueThisMonth   = await Db.Invoices
                                    .Where(i => i.Status == InvoiceStatus.Paid && i.CreatedAt >= monthStart)
                                    .SumAsync(i => (decimal?)i.Amount) ?? 0m;
        var pendingOrders      = await Db.Orders
                                    .CountAsync(o => o.Status == OrderStatus.Created
                                                  || o.Status == OrderStatus.Sending);

        var recentInquiries = await Db.Bookings
            .Include(b => b.Listing)
            .Include(b => b.User)
            .Where(b => b.Status == BookingStatus.Pending)
            .OrderByDescending(b => b.CreatedAt)
            .Take(5)
            .Select(b => new RecentInquiryDto(
                b.Id,
                b.User.Name,
                b.User.Email,
                b.Listing.Title,
                b.Listing.Type.ToString().ToLower(),
                b.CreatedAt.ToString("yyyy-MM-dd"),
                "new",
                b.Notes ?? ""
            ))
            .ToListAsync();

        return Ok(new AdminStatsDto(
            TotalListings:    totalListings,
            TotalOrders:      totalOrders,
            TotalUsers:       totalUsers,
            TotalRevenue:     totalRevenue,
            OrdersThisMonth:  ordersThisMonth,
            RevenueThisMonth: revenueThisMonth,
            PendingOrders:    pendingOrders,
            RecentInquiries:  recentInquiries
        ));
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
