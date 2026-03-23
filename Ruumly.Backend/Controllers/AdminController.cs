using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController(
    RuumlyDbContext db,
    IHttpClientFactory httpClientFactory,
    ILogger<AdminController> logger) : ControllerBase
{
    // ══════════════════════════════════════════════════════════════════════════
    // USERS
    // ══════════════════════════════════════════════════════════════════════════

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await db.Users
            .OrderByDescending(u => u.RegisteredAt)
            .ToListAsync();
        return Ok(users.Select(MapUser));
    }

    [HttpGet("users/{id:guid}")]
    public async Task<IActionResult> GetUser(Guid id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound(Error("User not found"));
        return Ok(MapUser(user));
    }

    [HttpPatch("users/{id:guid}/status")]
    public async Task<IActionResult> UpdateUserStatus(Guid id, [FromBody] UpdateUserStatusRequest body)
    {
        if (!Enum.TryParse<UserStatus>(body.Status, ignoreCase: true, out var status))
            return BadRequest(Error($"Invalid status '{body.Status}'. Use 'active' or 'blocked'."));

        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound(Error("User not found"));

        var prev = user.Status;
        user.Status = status;
        await Audit("user.status_changed", User.GetUserEmail(),
            user.Name, $"{prev} → {status}");
        await db.SaveChangesAsync();

        return Ok(MapUser(user));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SUPPLIERS
    // ══════════════════════════════════════════════════════════════════════════

    [HttpGet("suppliers")]
    public async Task<IActionResult> GetSuppliers()
    {
        var suppliers = await db.Suppliers
            .Include(s => s.IntegrationSettings)
            .OrderBy(s => s.Name)
            .ToListAsync();

        // Batch compute order stats
        var orderStats = await db.Orders
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
            return MapSupplier(s, stats?.OrdersTotal ?? 0, stats?.Revenue ?? 0m, includeSettings: false);
        }));
    }

    [HttpGet("suppliers/{id:guid}")]
    public async Task<IActionResult> GetSupplier(Guid id)
    {
        var supplier = await db.Suppliers
            .Include(s => s.IntegrationSettings)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (supplier is null) return NotFound(Error("Supplier not found"));

        var stats = await db.Orders
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

        return Ok(MapSupplier(supplier, stats?.OrdersTotal ?? 0, stats?.Revenue ?? 0m, includeSettings: true));
    }

    [HttpPatch("suppliers/{id:guid}/status")]
    public async Task<IActionResult> UpdateSupplierStatus(Guid id, [FromBody] UpdateSupplierStatusRequest body)
    {
        var supplier = await db.Suppliers.FindAsync(id);
        if (supplier is null) return NotFound(Error("Supplier not found"));

        supplier.IsActive  = body.IsActive;
        supplier.UpdatedAt = DateTime.UtcNow;
        await Audit("supplier.status_changed", User.GetUserEmail(),
            supplier.Name, $"isActive → {body.IsActive}");
        await db.SaveChangesAsync();

        return Ok(new { supplier.Id, supplier.IsActive });
    }

    [HttpPost("suppliers/{id:guid}/test")]
    public async Task<IActionResult> TestSupplier(Guid id)
    {
        var supplier = await db.Suppliers
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
                if (!string.IsNullOrWhiteSpace(supplier.ApiAuthToken))
                {
                    if (string.Equals(supplier.ApiAuthType, "bearer", StringComparison.OrdinalIgnoreCase))
                        client.DefaultRequestHeaders.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", supplier.ApiAuthToken);
                    else if (string.Equals(supplier.ApiAuthType, "apikey", StringComparison.OrdinalIgnoreCase))
                        client.DefaultRequestHeaders.Add("X-API-Key", supplier.ApiAuthToken);
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
            await db.SaveChangesAsync();
        }

        return Ok(new { success, latencyMs });
    }

    // ══════════════════════════════════════════════════════════════════════════
    // INTEGRATION SETTINGS
    // ══════════════════════════════════════════════════════════════════════════

    [HttpGet("integrations")]
    public async Task<IActionResult> GetIntegrations()
    {
        var settings = await db.IntegrationSettings
            .Include(i => i.Supplier)
            .OrderBy(i => i.Supplier.Name)
            .ToListAsync();
        return Ok(settings.Select(MapIntegrationSettings));
    }

    [HttpPatch("integrations/{id:guid}")]
    public async Task<IActionResult> PatchIntegration(Guid id, [FromBody] PatchIntegrationSettingsRequest body)
    {
        var settings = await db.IntegrationSettings
            .Include(i => i.Supplier)
            .FirstOrDefaultAsync(i => i.Id == id);
        if (settings is null) return NotFound(Error("Integration settings not found"));

        if (body.ApprovalMode is not null &&
            Enum.TryParse<ApprovalMode>(body.ApprovalMode, ignoreCase: true, out var am))
            settings.ApprovalMode = am;

        if (body.PostingMode is not null &&
            Enum.TryParse<PostingMode>(body.PostingMode, ignoreCase: true, out var pm))
            settings.PostingMode = pm;

        if (body.FallbackPostingMode is not null &&
            Enum.TryParse<PostingMode>(body.FallbackPostingMode, ignoreCase: true, out var fpm))
            settings.FallbackPostingMode = fpm;

        if (body.MappingProfile is not null)
            settings.MappingProfile = body.MappingProfile;

        if (body.IsActive.HasValue)
            settings.IsActive = body.IsActive.Value;

        settings.UpdatedAt = DateTime.UtcNow;

        await Audit("integration.updated", User.GetUserEmail(),
            settings.Supplier.Name, $"Settings updated");
        await db.SaveChangesAsync();

        return Ok(MapIntegrationSettings(settings));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ROUTING RULES
    // ══════════════════════════════════════════════════════════════════════════

    [HttpGet("routing-rules")]
    public async Task<IActionResult> GetRoutingRules()
    {
        var rules = await db.OrderRoutingRules
            .OrderByDescending(r => r.Priority)
            .ToListAsync();
        return Ok(rules.Select(MapRoutingRule));
    }

    [HttpPost("routing-rules")]
    public async Task<IActionResult> CreateRoutingRule([FromBody] CreateRoutingRuleRequest body)
    {
        ListingType? serviceType = null;
        if (!string.IsNullOrWhiteSpace(body.ServiceType) &&
            Enum.TryParse<ListingType>(body.ServiceType, ignoreCase: true, out var st))
            serviceType = st;

        if (!Enum.TryParse<PostingMode>(body.PostingChannel, ignoreCase: true, out var pc))
            return BadRequest(Error($"Invalid postingChannel '{body.PostingChannel}'"));

        var rule = new OrderRoutingRule
        {
            Id               = Guid.NewGuid(),
            Name             = body.Name,
            SupplierId       = body.SupplierId,
            ServiceType      = serviceType,
            OrderType        = body.OrderType,
            PriceThreshold   = body.PriceThreshold,
            CustomerType     = body.CustomerType,
            RequiresApproval = body.RequiresApproval,
            ApproverRole     = body.ApproverRole,
            PostingChannel   = pc,
            Priority         = body.Priority,
            IsActive         = body.IsActive,
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow,
        };

        db.OrderRoutingRules.Add(rule);
        await Audit("routing_rule.created", User.GetUserEmail(), body.Name, null);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetRoutingRules), new { }, MapRoutingRule(rule));
    }

    [HttpPatch("routing-rules/{id:guid}")]
    public async Task<IActionResult> PatchRoutingRule(Guid id, [FromBody] PatchRoutingRuleRequest body)
    {
        var rule = await db.OrderRoutingRules.FindAsync(id);
        if (rule is null) return NotFound(Error("Routing rule not found"));

        if (body.Name is not null) rule.Name = body.Name;
        if (body.OrderType is not null) rule.OrderType = body.OrderType;
        if (body.CustomerType is not null) rule.CustomerType = body.CustomerType;
        if (body.PriceThreshold.HasValue) rule.PriceThreshold = body.PriceThreshold;
        if (body.RequiresApproval.HasValue) rule.RequiresApproval = body.RequiresApproval.Value;
        if (body.ApproverRole is not null) rule.ApproverRole = body.ApproverRole;
        if (body.Priority.HasValue) rule.Priority = body.Priority.Value;
        if (body.IsActive.HasValue) rule.IsActive = body.IsActive.Value;

        if (body.ServiceType is not null)
            rule.ServiceType = Enum.TryParse<ListingType>(body.ServiceType, ignoreCase: true, out var st)
                ? st : (ListingType?)null;

        if (body.PostingChannel is not null &&
            Enum.TryParse<PostingMode>(body.PostingChannel, ignoreCase: true, out var pc))
            rule.PostingChannel = pc;

        rule.UpdatedAt = DateTime.UtcNow;
        await Audit("routing_rule.updated", User.GetUserEmail(), rule.Name, null);
        await db.SaveChangesAsync();

        return Ok(MapRoutingRule(rule));
    }

    [HttpDelete("routing-rules/{id:guid}")]
    public async Task<IActionResult> DeleteRoutingRule(Guid id)
    {
        var rule = await db.OrderRoutingRules.FindAsync(id);
        if (rule is null) return NotFound(Error("Routing rule not found"));

        db.OrderRoutingRules.Remove(rule);
        await Audit("routing_rule.deleted", User.GetUserEmail(), rule.Name, null);
        await db.SaveChangesAsync();

        return NoContent();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AUDIT LOG
    // ══════════════════════════════════════════════════════════════════════════

    [HttpGet("audit-log")]
    public async Task<IActionResult> GetAuditLog([FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        page  = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 200);

        var total = await db.AuditLogs.CountAsync();
        var items = await db.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

        return Ok(new
        {
            data  = items.Select(MapAuditLog),
            total,
            page,
            limit,
            hasMore = (page - 1) * limit + items.Count < total,
        });
    }

    // ══════════════════════════════════════════════════════════════════════════
    // STATS
    // ══════════════════════════════════════════════════════════════════════════

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var totalListings      = await db.Listings.CountAsync(l => l.IsActive);
        var totalOrders        = await db.Orders.CountAsync();
        var totalUsers         = await db.Users.CountAsync(u => u.Role != UserRole.Admin);
        var totalRevenue       = await db.Invoices
                                    .Where(i => i.Status == InvoiceStatus.Paid)
                                    .SumAsync(i => (decimal?)i.Amount) ?? 0m;
        var ordersThisMonth    = await db.Orders
                                    .CountAsync(o => o.CreatedAt >= monthStart);
        var revenueThisMonth   = await db.Invoices
                                    .Where(i => i.Status == InvoiceStatus.Paid && i.CreatedAt >= monthStart)
                                    .SumAsync(i => (decimal?)i.Amount) ?? 0m;
        var pendingOrders      = await db.Orders
                                    .CountAsync(o => o.Status == OrderStatus.Created
                                                  || o.Status == OrderStatus.Sending);

        return Ok(new AdminStatsDto(
            TotalListings:    totalListings,
            TotalOrders:      totalOrders,
            TotalUsers:       totalUsers,
            TotalRevenue:     totalRevenue,
            OrdersThisMonth:  ordersThisMonth,
            RevenueThisMonth: revenueThisMonth,
            PendingOrders:    pendingOrders
        ));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private async Task Audit(string action, string actor, string target, string? detail)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Id        = Guid.NewGuid(),
            Action    = action,
            Actor     = actor,
            Target    = target,
            Detail    = detail,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static object Error(string message) =>
        new { error = "Not Found", message, statusCode = 404 };

    // ─── Mappers ──────────────────────────────────────────────────────────────

    private static UserDto MapUser(Models.User u) => new(
        u.Id, u.Name, u.Email, u.Role, u.Status,
        u.Company, u.Phone, u.Avatar,
        u.RegisteredAt, u.LastLoginAt, u.BookingsCount);

    private static SupplierDto MapSupplier(
        Models.Supplier s, int ordersTotal, decimal revenue, bool includeSettings) => new(
        Id:                  s.Id,
        Name:                s.Name,
        RegistryCode:        s.RegistryCode,
        ContactName:         s.ContactName,
        ContactEmail:        s.ContactEmail,
        ContactPhone:        s.ContactPhone,
        IntegrationType:     s.IntegrationType.ToString().ToLower(),
        ApiEndpoint:         s.ApiEndpoint,
        ApiAuthType:         s.ApiAuthType,
        RecipientEmail:      s.RecipientEmail,
        IsActive:            s.IsActive,
        IntegrationHealth:   s.IntegrationHealth.ToString().ToLower(),
        Notes:               s.Notes,
        CreatedAt:           s.CreatedAt.ToString("yyyy-MM-dd"),
        UpdatedAt:           s.UpdatedAt.ToString("yyyy-MM-dd"),
        OrdersTotal:         ordersTotal,
        Revenue:             revenue,
        IntegrationSettings: includeSettings && s.IntegrationSettings is not null
            ? MapIntegrationSettings(s.IntegrationSettings)
            : null);

    private static IntegrationSettingsDto MapIntegrationSettings(Models.IntegrationSettings i) => new(
        Id:                  i.Id,
        SupplierId:          i.SupplierId,
        SupplierName:        i.Supplier?.Name ?? string.Empty,
        ApprovalMode:        i.ApprovalMode.ToString().ToLower(),
        PostingMode:         i.PostingMode.ToString().ToLower(),
        FallbackPostingMode: i.FallbackPostingMode.ToString().ToLower(),
        MappingProfile:      i.MappingProfile,
        LastTestedAt:        i.LastTestedAt?.ToString("yyyy-MM-dd HH:mm"),
        LastTestResult:      i.LastTestResult,
        IsActive:            i.IsActive,
        UpdatedAt:           i.UpdatedAt.ToString("yyyy-MM-dd HH:mm"));

    private static RoutingRuleDto MapRoutingRule(Models.OrderRoutingRule r) => new(
        Id:               r.Id,
        Name:             r.Name,
        SupplierId:       r.SupplierId,
        ServiceType:      r.ServiceType?.ToString().ToLower(),
        OrderType:        r.OrderType,
        PriceThreshold:   r.PriceThreshold,
        CustomerType:     r.CustomerType,
        RequiresApproval: r.RequiresApproval,
        ApproverRole:     r.ApproverRole,
        PostingChannel:   r.PostingChannel.ToString().ToLower(),
        Priority:         r.Priority,
        IsActive:         r.IsActive,
        CreatedAt:        r.CreatedAt.ToString("yyyy-MM-dd"),
        UpdatedAt:        r.UpdatedAt.ToString("yyyy-MM-dd"));

    private static AuditLogDto MapAuditLog(Models.AuditLog a) => new(
        Id:        a.Id,
        Action:    a.Action,
        Actor:     a.Actor,
        Target:    a.Target,
        Detail:    a.Detail,
        CreatedAt: a.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
}
