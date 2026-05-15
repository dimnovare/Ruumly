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

        var created = new List<object>();

        foreach (var supplier in rebateSuppliers)
        {
            // Skip if already generated for this period
            var existing = await Db.RebateInvoices
                .FirstOrDefaultAsync(r => r.SupplierId == supplier.Id && r.Period == period);
            if (existing is not null)
                continue;

            // Sum margin from PayoutEntries in this period
            var stats = await Db.PayoutEntries
                .Where(p => p.SupplierId == supplier.Id &&
                            p.CreatedAt >= period &&
                            p.CreatedAt < periodEnd &&
                            p.Status != PayoutStatus.Cancelled)
                .GroupBy(p => p.SupplierId)
                .Select(g => new { TotalMargin = g.Sum(p => p.PlatformMargin), OrderCount = g.Count() })
                .FirstOrDefaultAsync();

            if (stats is null || stats.OrderCount == 0)
                continue;

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
                supplierName = r.Supplier.Name,
                period       = r.Period.ToString("yyyy-MM"),
                r.TotalMargin,
                r.OrderCount,
                status       = r.Status.ToString().ToLower(),
                sentAt       = r.SentAt,
                paidAt       = r.PaidAt,
                r.Notes,
                r.CreatedAt,
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

    /// <summary>Provider's own rebate invoices. Admin may pass ?supplierId= to view any supplier.</summary>
    [HttpGet("/api/supplier/rebate-invoices")]
    [Authorize(Roles = "Provider,Admin")]
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
            var userId = User.GetUserId();
            var user   = await Db.Users.FindAsync(userId);
            supplierId = user?.SupplierId;
        }

        if (supplierId is null)
            return BadRequest(Error("No supplier linked."));

        var invoices = await Db.RebateInvoices
            .Where(r => r.SupplierId == supplierId)
            .OrderByDescending(r => r.Period)
            .Take(24)   // last 2 years of monthly invoices
            .Select(r => new {
                r.Id,
                period      = r.Period.ToString("yyyy-MM"),
                totalMargin = r.TotalMargin,
                status      = r.Status.ToString().ToLower(),
                r.SentAt,
                r.PaidAt,
            })
            .ToListAsync();

        return Ok(invoices);
    }
}

public record MarkRebatePaidRequest(string? Notes);

public record GenerateRebateRequest(string? Period, int? Year, int? Month);
