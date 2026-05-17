using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Controllers;

[Route("api/admin")]
public class AdminPayoutsController(RuumlyDbContext db) : AdminBaseController(db)
{
    [HttpGet("payouts")]
    public async Task<IActionResult> GetPayouts(
        [FromQuery] string? status     = null,
        [FromQuery] Guid?   supplierId = null,
        [FromQuery] int     page       = 1,
        [FromQuery] int     limit      = 100)
    {
        page  = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 500);

        // Build the base filter ONCE — summary totals and paginated entries
        // both derive from this so they can never diverge.
        var baseQuery = Db.PayoutEntries.AsQueryable();
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<PayoutStatus>(status, true, out var s))
            baseQuery = baseQuery.Where(p => p.Status == s);
        if (supplierId.HasValue)
            baseQuery = baseQuery.Where(p => p.SupplierId == supplierId);

        // Summary uses the full filtered set (no pagination).
        var totalPending = await baseQuery
            .Where(p => p.Status == PayoutStatus.Pending)
            .SumAsync(p => (decimal?)p.SupplierAmount) ?? 0m;
        var totalPaid    = await baseQuery
            .Where(p => p.Status == PayoutStatus.Paid)
            .SumAsync(p => (decimal?)p.SupplierAmount) ?? 0m;
        var totalMargin  = await baseQuery
            .SumAsync(p => (decimal?)p.PlatformMargin) ?? 0m;

        var total   = await baseQuery.CountAsync();
        var entries = await baseQuery
            .Include(p => p.Supplier)
            .Include(p => p.Order)
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(p => new {
                p.Id,
                p.SupplierId,
                supplierName     = p.Supplier.Name,
                p.OrderId,
                p.SupplierAmount,
                p.PlatformMargin,
                status           = p.Status.ToString().ToLower(),
                p.PaidAt,
                p.PaymentReference,
                p.CreatedAt,
            })
            .ToListAsync();

        return Ok(new {
            entries,
            summary = new { totalPending, totalPaid, totalMargin },
            total,
            page,
            limit,
            hasMore = (page - 1) * limit + entries.Count < total,
        });
    }

    [HttpPatch("payouts/{id:guid}/mark-paid")]
    public async Task<IActionResult> MarkPaid(Guid id, [FromBody] MarkPayoutPaidRequest request)
    {
        var entry = await Db.PayoutEntries.FindAsync(id);
        if (entry is null) return NotFound();

        entry.Status           = PayoutStatus.Paid;
        entry.PaidAt           = DateTime.UtcNow;
        entry.PaymentReference = request.Reference;
        await Db.SaveChangesAsync();

        return Ok(new { entry.Id, status = "paid" });
    }
}

public record MarkPayoutPaidRequest(string? Reference);
