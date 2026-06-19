using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Controllers;

[Route("api/admin")]
public class AdminRebateController(RuumlyDbContext db) : AdminBaseController(db)
{
    /// <summary>
    /// Generate monthly rebate invoices for all rebate-model suppliers.
    /// Aggregates PayoutEntry.PlatformMargin for the given month.
    /// Idempotent: existing invoices for the period are skipped.
    /// </summary>
    [HttpPost("rebate-invoices/generate")]
    public async Task<IActionResult> GenerateMonthly(
        [FromQuery] int? year,
        [FromQuery] int? month,
        [FromBody] GenerateRebateRequest? body = null)
    {
        // Resolve year/month from either query or body.period (e.g. "2026-04")
        int resolvedYear  = year  ?? body?.Year  ?? 0;
        int resolvedMonth = month ?? body?.Month ?? 0;

        if ((resolvedYear == 0 || resolvedMonth == 0) && !string.IsNullOrWhiteSpace(body?.Period))
        {
            var parts = body.Period.Split('-');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var y) &&
                int.TryParse(parts[1], out var m))
            {
                resolvedYear  = y;
                resolvedMonth = m;
            }
        }

        if (resolvedYear < 2020 || resolvedYear > 2100 || resolvedMonth < 1 || resolvedMonth > 12)
            return BadRequest(Error("Invalid year or month."));

        var period    = new DateTime(resolvedYear, resolvedMonth, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = period.AddMonths(1);

        var rebateSuppliers = await Db.Suppliers
            .Where(s => s.BillingModel == BillingModel.Rebate && s.IsActive)
            .ToListAsync();

        // Pre-load in two batch queries instead of 2 queries × N suppliers in a loop.
        var supplierIds = rebateSuppliers.Select(s => s.Id).ToList();

        // Which suppliers already have an invoice for this period? (idempotency check)
        var existingInvoiceSupplierIds = (await Db.RebateInvoices
            .Where(r => supplierIds.Contains(r.SupplierId) && r.Period == period)
            .Select(r => r.SupplierId)
            .ToListAsync())
            .ToHashSet();

        // Aggregate payout stats for all due suppliers in one round trip.
        var payoutStats = await Db.PayoutEntries
            .Where(p => supplierIds.Contains(p.SupplierId)
                     && p.CreatedAt >= period
                     && p.CreatedAt < periodEnd
                     && p.Status != PayoutStatus.Cancelled)
            .GroupBy(p => p.SupplierId)
            .Select(g => new
            {
                SupplierId  = g.Key,
                TotalMargin = g.Sum(p => p.PlatformMargin),
                OrderCount  = g.Count(),
            })
            .ToDictionaryAsync(x => x.SupplierId);

        // Iterate in memory — zero additional DB calls inside the loop.
        var created = new List<object>();

        foreach (var supplier in rebateSuppliers)
        {
            if (existingInvoiceSupplierIds.Contains(supplier.Id)) continue;

            if (!payoutStats.TryGetValue(supplier.Id, out var stats)
                || stats.OrderCount == 0) continue;

            var invoice = new RebateInvoice
            {
                Id          = Guid.NewGuid(),
                SupplierId  = supplier.Id,
                Period      = period,
                TotalMargin = stats.TotalMargin,
                OrderCount  = stats.OrderCount,
                Status      = RebateInvoiceStatus.Draft,
                CreatedAt   = DateTime.UtcNow,
                UpdatedAt   = DateTime.UtcNow,
            };

            Db.RebateInvoices.Add(invoice);
            created.Add(new
            {
                invoice.Id,
                supplierId   = supplier.Id,
                supplierName = supplier.Name,
                invoice.TotalMargin,
                invoice.OrderCount,
            });
        }

        await Db.SaveChangesAsync();

        return Ok(new { generated = created.Count, invoices = created });
    }

    /// <summary>
    /// List rebate invoices, optionally filtered by supplier or status.
    /// </summary>
    [HttpGet("rebate-invoices")]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? supplierId = null,
        [FromQuery] string? status   = null,
        [FromQuery] int page         = 1,
        [FromQuery] int limit        = 50)
    {
        page  = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);

        var query = Db.RebateInvoices
            .Include(r => r.Supplier)
            .AsQueryable();

        if (supplierId.HasValue)
            query = query.Where(r => r.SupplierId == supplierId);

        if (!string.IsNullOrEmpty(status) &&
            Enum.TryParse<RebateInvoiceStatus>(status, true, out var s))
            query = query.Where(r => r.Status == s);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(r => r.Period)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(r => new {
                r.Id,
                r.SupplierId,
                supplierName      = r.Supplier.Name,
                period            = r.Period.ToString("yyyy-MM"),
                bookingsCount     = r.OrderCount,
                bookingCount      = r.OrderCount,
                totalValue        = (decimal?)null,
                rebateAmount      = r.TotalMargin,
                status            = r.Status.ToString().ToLower(),
                sentAt            = r.SentAt,
                paidAt            = r.PaidAt,
                r.Notes,
                createdAt         = r.CreatedAt.ToString("yyyy-MM-dd"),
            })
            .ToListAsync();

        return Ok(new { total, page, limit, items });
    }

    /// <summary>Mark a rebate invoice as Sent (emailed to supplier).</summary>
    [HttpPatch("rebate-invoices/{id:guid}/mark-sent")]
    [HttpPatch("rebate-invoices/{id:guid}/sent")]
    public async Task<IActionResult> MarkSent(Guid id)
    {
        var invoice = await Db.RebateInvoices.FindAsync(id);
        if (invoice is null) return NotFound(Error("Rebate invoice not found."));

        invoice.Status    = RebateInvoiceStatus.Sent;
        invoice.SentAt    = DateTime.UtcNow;
        invoice.UpdatedAt = DateTime.UtcNow;
        await Db.SaveChangesAsync();

        return Ok(new { invoice.Id, status = "sent" });
    }

    /// <summary>Mark a rebate invoice as Paid (supplier settled).</summary>
    [HttpPatch("rebate-invoices/{id:guid}/mark-paid")]
    [HttpPatch("rebate-invoices/{id:guid}/paid")]
    public async Task<IActionResult> MarkPaid(Guid id, [FromBody] MarkRebatePaidRequest request)
    {
        var invoice = await Db.RebateInvoices.FindAsync(id);
        if (invoice is null) return NotFound(Error("Rebate invoice not found."));

        invoice.Status    = RebateInvoiceStatus.Paid;
        invoice.PaidAt    = DateTime.UtcNow;
        invoice.Notes     = request.Notes ?? invoice.Notes;
        invoice.UpdatedAt = DateTime.UtcNow;
        await Db.SaveChangesAsync();

        return Ok(new { invoice.Id, status = "paid" });
    }

}

/// <summary>
/// Provider-facing rebate invoices. Lives OUTSIDE AdminBaseController so the
/// Admin-only class filter does not apply — a Provider must be able to read
/// their own rebate invoices. Admin may pass ?supplierId= to impersonate.
/// </summary>
[ApiController]
[Route("api/supplier/rebate-invoices")]
[Authorize(Roles = "Provider,Admin")]
public class SupplierRebateController(RuumlyDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetForSupplier()
    {
        Guid? supplierId;
        if (User.IsInRole("Admin"))
        {
            var sv = Request.Query["supplierId"].FirstOrDefault();
            supplierId = sv is not null ? Guid.Parse(sv) : null;
        }
        else
        {
            var user = await db.Users.FindAsync(User.GetUserId());
            supplierId = user?.SupplierId;
        }

        if (supplierId is null)
            return BadRequest(new { error = "No supplier linked." });

        var invoices = await db.RebateInvoices
            .Where(r => r.SupplierId == supplierId)
            .OrderByDescending(r => r.Period)
            .Take(24)   // last 2 years of monthly invoices
            .Select(r => new {
                r.Id,
                period        = r.Period.ToString("yyyy-MM"),
                bookingsCount = r.OrderCount,
                totalValue    = (decimal?)null,
                rebateAmount  = r.TotalMargin,
                status        = r.Status.ToString().ToLower(),
                dueDate       = (string?)null,
                sentAt        = r.SentAt,
                paidAt        = r.PaidAt,
                createdAt     = r.CreatedAt.ToString("yyyy-MM-dd"),
            })
            .ToListAsync();

        return Ok(invoices);
    }
}

public record MarkRebatePaidRequest(string? Notes);

public record GenerateRebateRequest(string? Period, int? Year, int? Month);
