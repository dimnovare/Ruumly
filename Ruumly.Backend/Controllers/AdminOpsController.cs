using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Controllers;

/// <summary>
/// Operational health and stuck-order views for the ops/support team.
/// All endpoints require Admin role (inherited from AdminBaseController).
/// </summary>
[Route("api/admin/ops")]
public class AdminOpsController(RuumlyDbContext db) : AdminBaseController(db)
{
    // ── GET /api/admin/ops/stuck ───────────────────────────────────────────────
    /// <summary>
    /// Returns orders stuck in Sending or Pending for > 30 minutes,
    /// invoices awaiting a refund, and a count of failed webhooks (always 0 —
    /// no webhook table yet; reserved for future use).
    /// </summary>
    [HttpGet("stuck")]
    public async Task<IActionResult> GetStuck()
    {
        var threshold = DateTime.UtcNow.AddMinutes(-30);

        var stuckOrders = await Db.Orders
            .Include(o => o.Supplier)
            .Include(o => o.Booking)
                .ThenInclude(b => b.Invoice)
            .Where(o => (o.Status == OrderStatus.Sending || o.Status == OrderStatus.Created)
                     && o.CreatedAt < threshold)
            .OrderBy(o => o.CreatedAt)
            .Select(o => new
            {
                orderId           = o.Id,
                bookingId         = o.BookingId,
                status            = o.Status.ToString(),
                stuckSinceMinutes = (int)(DateTime.UtcNow - o.CreatedAt).TotalMinutes,
                invoiceStatus     = o.Booking.Invoice != null
                                        ? o.Booking.Invoice.Status.ToString()
                                        : (string?)null,
                supplierId        = o.SupplierId,
                supplierName      = o.Supplier.Name,
                amount            = o.Total,
                createdAt         = o.CreatedAt,
            })
            .ToListAsync();

        var pendingRefunds = await Db.Invoices
            .Include(i => i.Booking)
            .Where(i => i.Status == InvoiceStatus.PendingRefund)
            .OrderBy(i => i.CreatedAt)
            .Select(i => new
            {
                invoiceId   = i.Id,
                bookingId   = i.BookingId,
                amount      = i.Amount,
                cancelledAt = i.Booking.UpdatedAt,   // closest proxy for cancellation time
            })
            .ToListAsync();

        return Ok(new
        {
            stuckOrders,
            pendingRefunds,
            failedWebhooks = 0,
        });
    }

    // ── GET /api/admin/ops/health ──────────────────────────────────────────────
    /// <summary>
    /// Quick operational health snapshot.  All values are computed live from the DB —
    /// no caching so ops always see the current state.
    /// </summary>
    [HttpGet("health")]
    public async Task<IActionResult> GetHealth()
    {
        var threshold24h = DateTime.UtcNow.AddHours(-24);
        var threshold30m = DateTime.UtcNow.AddMinutes(-30);

        var activeListings      = await Db.Listings.CountAsync(l => l.IsActive);
        var pendingApplications = await Db.Suppliers.CountAsync(s => !s.IsActive);
        var openOrders          = await Db.Orders.CountAsync(
            o => o.Status != OrderStatus.Completed && o.Status != OrderStatus.Cancelled);
        var stuckOrders         = await Db.Orders.CountAsync(
            o => (o.Status == OrderStatus.Sending || o.Status == OrderStatus.Created)
              && o.CreatedAt < threshold30m);
        var pendingRefunds      = await Db.Invoices.CountAsync(
            i => i.Status == InvoiceStatus.PendingRefund);

        // Last Hangfire job — query raw table; gracefully return null if schema differs.
        DateTime? lastHangfireJobAt = null;
        try
        {
            lastHangfireJobAt = await Db.Database
                .SqlQueryRaw<DateTime>(
                    "SELECT \"CreatedAt\" FROM \"HangFire\".\"Job\" ORDER BY \"CreatedAt\" DESC LIMIT 1")
                .FirstOrDefaultAsync();
            if (lastHangfireJobAt == default(DateTime))
                lastHangfireJobAt = null;
        }
        catch
        {
            // Hangfire schema may not exist in local dev or may use a different schema name
            lastHangfireJobAt = null;
        }

        return Ok(new
        {
            activeListings,
            pendingApplications,
            openOrders,
            stuckOrders,
            pendingRefunds,
            failedEmailsLast24h = 0,   // no dedicated email-failure table yet
            lastHangfireJobAt,
        });
    }
}
