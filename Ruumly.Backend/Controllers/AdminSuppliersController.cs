using Asp.Versioning;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Controllers;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin")]
public class AdminSuppliersController(
    RuumlyDbContext db,
    IHttpClientFactory httpClientFactory,
    ILogger<AdminSuppliersController> logger,
    TokenProtector tokenProtector) : AdminBaseController(db)
{
    [HttpGet("suppliers")]
    public async Task<IActionResult> GetSuppliers()
    {
        var suppliers = await Db.Suppliers
            .Include(s => s.IntegrationSettings)
            .OrderBy(s => s.Name)
            .ToListAsync();

        var orderStats = await Db.Orders
            .GroupBy(o => o.SupplierId)
            .Select(g => new
            {
                SupplierId  = g.Key,
                OrdersTotal = g.Count(),
                Revenue     = g.Where(o => o.Status == OrderStatus.Confirmed
                                        || o.Status == OrderStatus.Completed)
                               .Sum(o => (decimal?)o.Total) ?? 0m,
            })
            .ToDictionaryAsync(x => x.SupplierId);

        return Ok(suppliers.Select(s =>
        {
            orderStats.TryGetValue(s.Id, out var stats);
            return AdminMappers.MapSupplier(s, stats?.OrdersTotal ?? 0, stats?.Revenue ?? 0m, includeSettings: false);
        }));
    }

    [HttpGet("suppliers/{id:guid}")]
    public async Task<IActionResult> GetSupplier(Guid id)
    {
        var supplier = await Db.Suppliers
            .Include(s => s.IntegrationSettings)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (supplier is null) return NotFound(Error("Supplier not found"));

        var stats = await Db.Orders
            .Where(o => o.SupplierId == id)
            .GroupBy(o => o.SupplierId)
            .Select(g => new
            {
                OrdersTotal = g.Count(),
                Revenue     = g.Where(o => o.Status == OrderStatus.Confirmed
                                        || o.Status == OrderStatus.Completed)
                               .Sum(o => (decimal?)o.Total) ?? 0m,
            })
            .FirstOrDefaultAsync();

        return Ok(AdminMappers.MapSupplier(supplier, stats?.OrdersTotal ?? 0, stats?.Revenue ?? 0m, includeSettings: true));
    }

    [HttpPatch("suppliers/{id:guid}/status")]
    public async Task<IActionResult> UpdateSupplierStatus(Guid id, [FromBody] UpdateSupplierStatusRequest body)
    {
        var supplier = await Db.Suppliers.FindAsync(id);
        if (supplier is null) return NotFound(Error("Supplier not found"));

        supplier.IsActive  = body.IsActive;
        supplier.UpdatedAt = DateTime.UtcNow;
        await Audit("supplier.status_changed", User.GetUserEmail(),
            supplier.Name, $"isActive → {body.IsActive}");
        await Db.SaveChangesAsync();

        return Ok(new { supplier.Id, supplier.IsActive });
    }

    [HttpPatch("suppliers/{id:guid}/tier")]
    public async Task<IActionResult> UpdateSupplierTier(Guid id, [FromBody] UpdateSupplierTierRequest request)
    {
        var supplier = await Db.Suppliers.FindAsync(id);
        if (supplier is null)
            return NotFound(Error("Supplier not found."));

        if (!Enum.TryParse<SupplierTier>(request.Tier, ignoreCase: true, out var tier))
            return BadRequest(Error("Invalid tier. Use Starter, Standard, or Premium."));

        supplier.Tier       = tier;
        supplier.MonthlyFee = TierRules.MonthlyFee(tier);
        supplier.SubscriptionEndsAt =
            tier != SupplierTier.Starter
                ? DateTime.UtcNow.AddMonths(1)
                : null;
        supplier.UpdatedAt = DateTime.UtcNow;

        await Db.SaveChangesAsync();

        return Ok(new {
            id     = supplier.Id,
            tier   = supplier.Tier.ToString(),
            endsAt = supplier.SubscriptionEndsAt,
        });
    }

    [HttpPost("suppliers/{id:guid}/test")]
    public async Task<IActionResult> TestSupplier(Guid id)
    {
        var supplier = await Db.Suppliers
            .Include(s => s.IntegrationSettings)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (supplier is null) return NotFound(Error("Supplier not found"));

        bool   success    = false;
        long   latencyMs  = 0;
        string testResult = "not_api";

        if (supplier.IntegrationType == IntegrationType.Api &&
            !string.IsNullOrWhiteSpace(supplier.ApiEndpoint))
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var sw = Stopwatch.StartNew();
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Head, supplier.ApiEndpoint);
                var plainToken = tokenProtector.Unprotect(supplier.ApiAuthToken);
                if (!string.IsNullOrWhiteSpace(plainToken))
                {
                    if (string.Equals(supplier.ApiAuthType, "bearer", StringComparison.OrdinalIgnoreCase))
                        client.DefaultRequestHeaders.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", plainToken);
                    else if (string.Equals(supplier.ApiAuthType, "apikey", StringComparison.OrdinalIgnoreCase))
                        client.DefaultRequestHeaders.Add("X-API-Key", plainToken);
                }
                var response = await client.SendAsync(request);
                sw.Stop();
                success    = response.IsSuccessStatusCode;
                latencyMs  = sw.ElapsedMilliseconds;
                testResult = success ? "success" : $"http_{(int)response.StatusCode}";
            }
            catch (Exception ex)
            {
                sw.Stop();
                latencyMs  = sw.ElapsedMilliseconds;
                testResult = "error";
                logger.LogWarning("Supplier test failed for {SupplierId}: {Message}", id, ex.Message);
            }
        }

        if (supplier.IntegrationSettings is not null)
        {
            supplier.IntegrationSettings.LastTestedAt   = DateTime.UtcNow;
            supplier.IntegrationSettings.LastTestResult = testResult;
            supplier.IntegrationSettings.UpdatedAt      = DateTime.UtcNow;
            await Db.SaveChangesAsync();
        }

        return Ok(new { success, latencyMs });
    }
}
