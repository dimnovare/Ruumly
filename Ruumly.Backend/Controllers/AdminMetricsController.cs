using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Controllers;

/// <summary>
/// Health/metrics snapshot for the admin ops dashboard.
/// All endpoints require Admin role (inherited from AdminBaseController).
/// </summary>
[Route("api/admin/metrics")]
public class AdminMetricsController(RuumlyDbContext db) : AdminBaseController(db)
{
    // ── GET /api/admin/metrics ────────────────────────────────────────────────
    /// <summary>
    /// Returns a health/metrics snapshot: booking counts, payment success rates,
    /// contract signing status, and location publication status.
    /// All values are computed live from the DB — no caching.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMetrics()
    {
        var now   = DateTime.UtcNow;
        var h24   = now.AddHours(-24);
        var d7    = now.AddDays(-7);

        // ── Bookings ──────────────────────────────────────────────────────────
        var bookingsLast24h     = await Db.Bookings.CountAsync(b => b.CreatedAt >= h24);
        var bookingsLast7d      = await Db.Bookings.CountAsync(b => b.CreatedAt >= d7);
        var pendingPayment      = await Db.Bookings.CountAsync(b => b.Status == BookingStatus.Pending);
        var confirmedBookings   = await Db.Bookings.CountAsync(b => (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.AwaitingConfirmation)
                                                                  || b.Status == BookingStatus.Active
                                                                  || b.Status == BookingStatus.Completed);
        var cancelledBookings   = await Db.Bookings.CountAsync(b => b.Status == BookingStatus.Cancelled);

        // ── Payments (based on invoices) ──────────────────────────────────────
        var invoices24h         = await Db.Invoices
                                    .Where(i => i.CreatedAt >= h24)
                                    .Select(i => i.Status)
                                    .ToListAsync();

        var successLast24h      = invoices24h.Count(s => s == InvoiceStatus.Paid);
        var failedLast24h       = invoices24h.Count(s => s == InvoiceStatus.Overdue);

        var invoices7d          = await Db.Invoices
                                    .Where(i => i.CreatedAt >= d7)
                                    .Select(i => i.Status)
                                    .ToListAsync();

        var totalInvoices7d     = invoices7d.Count;
        var successRate7d       = totalInvoices7d > 0
                                    ? Math.Round((double)invoices7d.Count(s => s == InvoiceStatus.Paid) / totalInvoices7d, 2)
                                    : 1.0;

        // ── Contracts ─────────────────────────────────────────────────────────
        var pendingSignature    = await Db.SignedContracts.CountAsync(c => c.Status == "pending");
        var signedContracts     = await Db.SignedContracts.CountAsync(c => c.Status == "completed");

        // ── Locations ─────────────────────────────────────────────────────────
        var publishedLocations  = await Db.SupplierLocations.CountAsync(l => l.IsActive && !l.IsSynthetic);
        var unpublishedLocations = await Db.SupplierLocations.CountAsync(l => !l.IsActive && !l.IsSynthetic);

        // "Pending approval" is modelled as inactive suppliers awaiting activation
        var pendingApproval     = await Db.Suppliers.CountAsync(s => !s.IsActive);

        return Ok(new
        {
            bookings = new
            {
                last24h        = bookingsLast24h,
                last7d         = bookingsLast7d,
                pendingPayment,
                confirmed      = confirmedBookings,
                cancelled      = cancelledBookings,
            },
            payments = new
            {
                successLast24h,
                failedLast24h,
                successRate7d,
            },
            contracts = new
            {
                pendingSignature,
                signed         = signedContracts,
            },
            locations = new
            {
                published      = publishedLocations,
                unpublished    = unpublishedLocations,
                pendingApproval,
            },
        });
    }
}
