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
        [FromQuery] string? status = null,
        [FromQuery] Guid? supplierId = null)
    {
        var query = Db.PayoutEntries
            .Include(p => p.Supplier)
            .Include(p => p.Order)
            .AsQueryable();

        if (!string.IsNullOrEmpty(status) &&
            Enum.TryParse<PayoutStatus>(status, true, out var s))
            query = query.Where(p => p.Status == s);

        if (supplierId.HasValue)
            query = query.Where(p => p.SupplierId == supplierId);

        var entries = await query
            .OrderByDescending(p => p.CreatedAt)
            .Take(200)
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

        var summary = new {
            totalPending = entries.Where(e => e.status == "pending").Sum(e => e.SupplierAmount),
            totalPaid    = entries.Where(e => e.status == "paid").Sum(e => e.SupplierAmount),
            totalMargin  = entries.Sum(e => e.PlatformMargin),
        };

        return Ok(new { entries, summary });
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
