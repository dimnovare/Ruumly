using Asp.Versioning;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

[Route("api/admin")]
public class AdminSuppliersController(
    RuumlyDbContext db,
    IHttpClientFactory httpClientFactory,
    ILogger<AdminSuppliersController> logger,
    TokenProtector tokenProtector,
    IPricingConfigService pricingConfigService) : AdminBaseController(db)
{
    [HttpGet("suppliers")]
    public async Task<IActionResult> GetSuppliers([FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        page  = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);

        var total     = await Db.Suppliers.CountAsync();
        var suppliers = await Db.Suppliers
            .Include(s => s.IntegrationSettings)
            .OrderBy(s => s.Name)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

        // Only fetch order stats for the current page of suppliers
        var supplierIds = suppliers.Select(s => s.Id).ToList();
        var orderStats  = await Db.Orders
            .Where(o => supplierIds.Contains(o.SupplierId))
            .GroupBy(o => o.SupplierId)
            .Select(g => new { SupplierId = g.Key, OrdersTotal = g.Count() })
            .ToDictionaryAsync(x => x.SupplierId);

        var revenues = await Db.PayoutEntries
            .Where(p => supplierIds.Contains(p.SupplierId) && p.Status == PayoutStatus.Paid)
            .GroupBy(p => p.SupplierId)
            .Select(g => new { SupplierId = g.Key, Total = g.Sum(p => p.SupplierAmount) })
            .ToDictionaryAsync(x => x.SupplierId, x => x.Total);

        var pricingConfig = await pricingConfigService.GetAsync();
        var data = suppliers.Select(s =>
        {
            orderStats.TryGetValue(s.Id, out var stats);
            return AdminMappers.MapSupplier(s, stats?.OrdersTotal ?? 0,
                revenues.GetValueOrDefault(s.Id, 0m),
                includeSettings: false, pricingConfig: pricingConfig);
        }).ToList();

        return Ok(new PaginatedResult<SupplierDto>(
            data, total, page, limit,
            (page - 1) * limit + data.Count < total));
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
            .Select(g => new { OrdersTotal = g.Count() })
            .FirstOrDefaultAsync();

        var revenue = await Db.PayoutEntries
            .Where(p => p.SupplierId == id && p.Status == PayoutStatus.Paid)
            .SumAsync(p => (decimal?)p.SupplierAmount) ?? 0m;

        var pricingConfig = await pricingConfigService.GetAsync();
        return Ok(AdminMappers.MapSupplier(supplier, stats?.OrdersTotal ?? 0, revenue,
            includeSettings: true, pricingConfig: pricingConfig));
    }

    [HttpPost("suppliers")]
    public async Task<IActionResult> CreateSupplier([FromBody] CreateSupplierRequest body)
    {
        var supplier = new Supplier
        {
            Id                  = Guid.NewGuid(),
            Name                = body.Name,
            RegistryCode        = body.RegistryCode ?? "",
            ContactName         = body.ContactName ?? "",
            ContactEmail        = body.ContactEmail ?? "",
            ContactPhone        = body.ContactPhone ?? "",
            BillingModel        = Enum.TryParse<BillingModel>(body.BillingModel, true, out var bm)
                                  ? bm : BillingModel.Marketplace,
            IntegrationType     = Enum.TryParse<IntegrationType>(body.IntegrationType, true, out var it)
                                  ? it : IntegrationType.Manual,
            RecipientEmail      = body.RecipientEmail,
            ApiEndpoint         = body.ApiEndpoint,
            ApiAuthType         = body.ApiAuthType,
            ApiAuthToken        = !string.IsNullOrWhiteSpace(body.ApiAuthToken)
                                  ? tokenProtector.Protect(body.ApiAuthToken)
                                  : null,
            PartnerDiscountRate = body.PartnerDiscountRate ?? 0,
            ClientDiscountRate  = body.ClientDiscountRate ?? 0,
            Notes               = body.Notes,
            Iban                = body.Iban,
            BankAccountName     = body.BankAccountName,
            BankName            = body.BankName,
            IsActive            = true,
            CreatedAt           = DateTime.UtcNow,
            UpdatedAt           = DateTime.UtcNow,
        };

        Db.Suppliers.Add(supplier);
        await Audit("supplier.created", User.GetUserEmail(), supplier.Name, null);
        await Db.SaveChangesAsync();

        var pricingConfig = await pricingConfigService.GetAsync();
        return Ok(AdminMappers.MapSupplier(supplier, 0, 0m, false, pricingConfig));
    }

    [HttpPatch("suppliers/{id:guid}")]
    public async Task<IActionResult> UpdateSupplier(Guid id, [FromBody] UpdateSupplierRequest body)
    {
        var supplier = await Db.Suppliers.FindAsync(id);
        if (supplier is null) return NotFound(Error("Supplier not found."));

        if (body.Name is not null)             supplier.Name = body.Name;
        if (body.RegistryCode is not null)     supplier.RegistryCode = body.RegistryCode;
        if (body.ContactName is not null)      supplier.ContactName = body.ContactName;
        if (body.ContactEmail is not null)     supplier.ContactEmail = body.ContactEmail;
        if (body.ContactPhone is not null)     supplier.ContactPhone = body.ContactPhone;
        if (body.BillingModel is not null &&
            Enum.TryParse<BillingModel>(body.BillingModel, true, out var bm2))
            supplier.BillingModel = bm2;
        if (body.Tier is not null &&
            Enum.TryParse<SupplierTier>(body.Tier, true, out var tier))
        {
            var config = await pricingConfigService.GetAsync();
            supplier.Tier       = tier;
            supplier.MonthlyFee = config.ForTier(tier).MonthlyFee;
            supplier.SubscriptionEndsAt = tier != SupplierTier.Starter
                ? DateTime.UtcNow.AddMonths(1)
                : null;
        }
        if (body.IntegrationType is not null &&
            Enum.TryParse<IntegrationType>(body.IntegrationType, true, out var it2))
            supplier.IntegrationType = it2;
        if (body.RecipientEmail is not null)   supplier.RecipientEmail = body.RecipientEmail;
        if (body.ApiEndpoint is not null)      supplier.ApiEndpoint = body.ApiEndpoint;
        if (body.ApiAuthType is not null)      supplier.ApiAuthType = body.ApiAuthType;
        if (!string.IsNullOrWhiteSpace(body.ApiAuthToken))
            supplier.ApiAuthToken = tokenProtector.Protect(body.ApiAuthToken);
        if (body.PartnerDiscountRate.HasValue) supplier.PartnerDiscountRate = body.PartnerDiscountRate.Value;
        if (body.ClientDiscountRate.HasValue)  supplier.ClientDiscountRate = body.ClientDiscountRate.Value;
        if (body.Notes is not null)            supplier.Notes = body.Notes;
        if (body.Iban is not null)             supplier.Iban = body.Iban;
        if (body.BankAccountName is not null)  supplier.BankAccountName = body.BankAccountName;
        if (body.BankName is not null)         supplier.BankName = body.BankName;

        supplier.UpdatedAt = DateTime.UtcNow;
        await Audit("supplier.updated", User.GetUserEmail(), supplier.Name, null);
        await Db.SaveChangesAsync();

        var pricingConfig = await pricingConfigService.GetAsync();
        return Ok(AdminMappers.MapSupplier(supplier, 0, 0m, false, pricingConfig));
    }

    [HttpDelete("suppliers/{id:guid}")]
    public async Task<IActionResult> DeleteSupplier(Guid id)
    {
        var supplier = await Db.Suppliers.FindAsync(id);
        if (supplier is null) return NotFound(Error("Supplier not found."));

        supplier.IsActive  = false;
        supplier.UpdatedAt = DateTime.UtcNow;
        await Audit("supplier.deleted", User.GetUserEmail(), supplier.Name, null);
        await Db.SaveChangesAsync();

        return NoContent();
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

        var config = await pricingConfigService.GetAsync();
        supplier.Tier       = tier;
        supplier.MonthlyFee = config.ForTier(tier).MonthlyFee;
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

    [HttpPost("suppliers/{id}/approve-application")]
    public async Task<IActionResult> ApproveApplication(Guid id, [FromQuery] Guid userId)
    {
        var supplier = await Db.Suppliers.FindAsync(id);
        if (supplier is null) return NotFound(Error("Supplier not found"));

        var user = await Db.Users.FindAsync(userId);
        if (user is null) return NotFound(Error("User not found"));

        if (user.SupplierId.HasValue)
            return BadRequest(Error("User is already linked to a supplier"));

        supplier.IsActive  = true;
        supplier.UpdatedAt = DateTime.UtcNow;
        user.SupplierId    = supplier.Id;
        user.Role          = UserRole.Provider;

        await Db.SaveChangesAsync();

        var actorName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "admin";
        await Audit(
            "approve_supplier_application",
            actorName,
            $"Supplier:{supplier.Id}",
            $"Linked to user {user.Email}");

        return Ok(new { message = "Supplier approved and user linked as provider." });
    }
}
