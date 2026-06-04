using Asp.Versioning;
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Npgsql;
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
    IPricingConfigService pricingConfigService,
    ISupplierPollingService pollingService,
    IDistributedCache cache) : AdminBaseController(db)
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

        var listingCounts = await Db.Listings
            .Where(l => l.IsActive && supplierIds.Contains(l.SupplierId))
            .GroupBy(l => l.SupplierId)
            .Select(g => new { SupplierId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SupplierId, x => x.Count);

        var pricingConfig = await pricingConfigService.GetAsync();
        var data = suppliers.Select(s =>
        {
            orderStats.TryGetValue(s.Id, out var stats);
            return AdminMappers.MapSupplier(s, stats?.OrdersTotal ?? 0,
                revenues.GetValueOrDefault(s.Id, 0m),
                listingCounts.GetValueOrDefault(s.Id, 0),
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

        var listingCount  = await Db.Listings.CountAsync(l => l.IsActive && l.SupplierId == id);
        var pricingConfig = await pricingConfigService.GetAsync();
        return Ok(AdminMappers.MapSupplier(supplier, stats?.OrdersTotal ?? 0, revenue,
            listingCount, includeSettings: true, pricingConfig: pricingConfig));
    }

    [HttpPost("suppliers")]
    public async Task<IActionResult> CreateSupplier([FromBody] CreateSupplierRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Name))
            return BadRequest(new { error = ErrorMessages.Get("SUPPLIER_NAME_REQUIRED", Request.GetLang()) });

        if (!string.IsNullOrWhiteSpace(body.ApiEndpoint) && !OutboundEndpointValidator.IsAllowed(body.ApiEndpoint))
            return BadRequest(new { error = "Endpoint URL is not allowed (must be a public HTTPS host)." });

        var supplier = new Supplier
        {
            Id                  = Guid.NewGuid(),
            Name                = body.Name,
            RegistryCode        = string.IsNullOrWhiteSpace(body.RegistryCode) ? null : body.RegistryCode.Trim(),
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
            OnboardingStartedAt = DateTime.UtcNow,
            CreatedAt           = DateTime.UtcNow,
            UpdatedAt           = DateTime.UtcNow,
        };

        Db.Suppliers.Add(supplier);

        // Auto-create integration settings so supplier appears on Integrations tab
        Db.IntegrationSettings.Add(new IntegrationSettings
        {
            Id                  = Guid.NewGuid(),
            SupplierId          = supplier.Id,
            ApprovalMode        = ApprovalMode.Auto,
            PostingMode         = (PostingMode)(int)supplier.IntegrationType,
            FallbackPostingMode = PostingMode.Email,
            IsActive            = true,
            UpdatedAt           = DateTime.UtcNow,
        });

        Audit("supplier.created", User.GetUserEmail(), supplier.Name, null);
        try
        {
            await Db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
        {
            return Conflict(new { error = ErrorMessages.Get("REGISTRY_CODE_DUPLICATE", Request.GetLang()) });
        }

        var pricingConfig = await pricingConfigService.GetAsync();
        return Ok(AdminMappers.MapSupplier(supplier, 0, 0m, 0, false, pricingConfig));
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
        }
        if (body.IntegrationType is not null &&
            Enum.TryParse<IntegrationType>(body.IntegrationType, true, out var it2))
            supplier.IntegrationType = it2;
        if (body.RecipientEmail is not null)   supplier.RecipientEmail = body.RecipientEmail;
        if (body.ApiEndpoint is not null)
        {
            if (!string.IsNullOrWhiteSpace(body.ApiEndpoint) && !OutboundEndpointValidator.IsAllowed(body.ApiEndpoint))
                return BadRequest(new { error = "Endpoint URL is not allowed (must be a public HTTPS host)." });
            supplier.ApiEndpoint = body.ApiEndpoint;
        }
        if (body.ApiAuthType is not null)      supplier.ApiAuthType = body.ApiAuthType;
        if (!string.IsNullOrWhiteSpace(body.ApiAuthToken))
            supplier.ApiAuthToken = tokenProtector.Protect(body.ApiAuthToken);
        if (body.PartnerDiscountRate.HasValue) supplier.PartnerDiscountRate = body.PartnerDiscountRate.Value;
        if (body.ClientDiscountRate.HasValue)  supplier.ClientDiscountRate = body.ClientDiscountRate.Value;
        if (body.Notes is not null)            supplier.Notes = body.Notes;
        if (body.Iban is not null)             supplier.Iban = body.Iban;
        if (body.BankAccountName is not null)  supplier.BankAccountName = body.BankAccountName;
        if (body.BankName is not null)         supplier.BankName = body.BankName;

        // ── Partner page fields ───────────────────────────────────────────────
        if (body.IsPartnerPagePublished.HasValue)
            supplier.IsPartnerPagePublished = body.IsPartnerPagePublished.Value;
        if (body.Tagline is not null)         supplier.Tagline       = body.Tagline;
        if (body.LogoUrl is not null)         supplier.LogoUrl       = body.LogoUrl;
        if (body.HeroImageUrl is not null)    supplier.HeroImageUrl  = body.HeroImageUrl;
        if (body.WebsiteUrl is not null)      supplier.WebsiteUrl    = body.WebsiteUrl;
        if (body.FoundedYear.HasValue)        supplier.FoundedYear   = body.FoundedYear.Value;
        if (body.FoundingPartner.HasValue)    supplier.FoundingPartner = body.FoundingPartner.Value;
        if (body.IsVerified.HasValue)         supplier.IsVerified    = body.IsVerified.Value;
        if (body.GooglePlaceId is not null)   supplier.GooglePlaceId = body.GooglePlaceId;

        // Long description: merge individual language keys, preserving unlisted ones.
        if (body.LongDescriptionEt is not null ||
            body.LongDescriptionEn is not null ||
            body.LongDescriptionRu is not null)
        {
            var existing = new Dictionary<string, string?>();
            if (!string.IsNullOrEmpty(supplier.LongDescriptionTranslationsJson))
            {
                try
                {
                    existing = System.Text.Json.JsonSerializer
                        .Deserialize<Dictionary<string, string?>>(
                            supplier.LongDescriptionTranslationsJson) ?? existing;
                }
                catch { /* leave empty dict */ }
            }
            if (body.LongDescriptionEt is not null) existing["et"] = body.LongDescriptionEt;
            if (body.LongDescriptionEn is not null) existing["en"] = body.LongDescriptionEn;
            if (body.LongDescriptionRu is not null) existing["ru"] = body.LongDescriptionRu;
            supplier.LongDescriptionTranslationsJson =
                System.Text.Json.JsonSerializer.Serialize(existing);
        }

        // ── Polling fields ────────────────────────────────────────────────────
        if (body.PollingEnabled.HasValue)
            supplier.PollingEnabled = body.PollingEnabled.Value;
        if (body.PollingIntervalMinutes.HasValue &&
            new[] { 15, 30, 60, 360, 1440 }.Contains(body.PollingIntervalMinutes.Value))
            supplier.PollingIntervalMinutes = body.PollingIntervalMinutes.Value;
        // When enabling polling, schedule the first poll immediately if not yet scheduled.
        if (body.PollingEnabled == true && supplier.NextPollAt == null)
            supplier.NextPollAt = DateTime.UtcNow;
        if (body.PollingEndpoint is not null)
            supplier.PollingEndpoint = string.IsNullOrWhiteSpace(body.PollingEndpoint)
                ? null : body.PollingEndpoint;

        // Slug: validate uniqueness + shape, allow clearing.
        if (body.Slug is not null)
        {
            var slugValue = body.Slug.Trim().ToLower();
            string[] reserved = ["dashboard", "onboarding", "new", "edit", "admin", "api"];

            if (string.IsNullOrWhiteSpace(slugValue))
            {
                supplier.Slug = null;
            }
            else if (!System.Text.RegularExpressions.Regex.IsMatch(slugValue, "^[a-z0-9-]{2,80}$"))
            {
                return BadRequest(Error("Slug must be 2–80 lowercase letters, digits, or hyphens."));
            }
            else if (reserved.Contains(slugValue))
            {
                return BadRequest(Error($"'{slugValue}' is a reserved slug and cannot be used."));
            }
            else
            {
                var conflict = await Db.Suppliers
                    .AnyAsync(s => s.Slug == slugValue && s.Id != id);
                if (conflict)
                    return Conflict(Error($"Slug '{slugValue}' is already taken by another supplier."));
                supplier.Slug = slugValue;
            }
        }

        supplier.UpdatedAt = DateTime.UtcNow;
        Audit("supplier.updated", User.GetUserEmail(), supplier.Name, null);
        await Db.SaveChangesAsync();

        if (supplier.Slug is not null)
            await cache.RemoveAsync($"supplier:profile:{supplier.Slug}");

        var pricingConfig = await pricingConfigService.GetAsync();
        return Ok(AdminMappers.MapSupplier(supplier, 0, 0m, 0, false, pricingConfig));
    }

    [HttpDelete("suppliers/{id:guid}")]
    public async Task<IActionResult> DeleteSupplier(Guid id)
    {
        var supplier = await Db.Suppliers.FindAsync(id);
        if (supplier is null) return NotFound(Error("Supplier not found."));

        supplier.IsActive  = false;
        supplier.UpdatedAt = DateTime.UtcNow;
        Audit("supplier.deleted", User.GetUserEmail(), supplier.Name, null);
        await Db.SaveChangesAsync();

        return NoContent();
    }

    [HttpPatch("suppliers/{id:guid}/status")]
    public async Task<IActionResult> UpdateSupplierStatus(Guid id, [FromBody] UpdateSupplierStatusRequest body)
    {
        var supplier = await Db.Suppliers.FindAsync(id);
        if (supplier is null) return NotFound(Error("Supplier not found"));

        var wasInactive = !supplier.IsActive;
        supplier.IsActive  = body.IsActive;
        supplier.UpdatedAt = DateTime.UtcNow;

        // Auto-grant onboarding when supplier is first activated
        if (body.IsActive && wasInactive && supplier.OnboardingStartedAt is null)
            supplier.OnboardingStartedAt = DateTime.UtcNow;

        Audit("supplier.status_changed", User.GetUserEmail(),
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
        supplier.UpdatedAt  = DateTime.UtcNow;

        await Db.SaveChangesAsync();

        return Ok(new {
            id   = supplier.Id,
            tier = supplier.Tier.ToString(),
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
            // SSRF guard — refuse to probe loopback, private ranges, cloud metadata,
            // Railway internal, or non-HTTP(S) schemes via an admin-supplied endpoint.
            if (!OutboundEndpointValidator.IsAllowed(supplier.ApiEndpoint))
            {
                logger.LogWarning(
                    "SSRF guard blocked test for supplier {SupplierId} — endpoint '{Endpoint}' not allowed",
                    id, supplier.ApiEndpoint);
                return BadRequest(new { error = "Endpoint URL is not allowed (must be a public HTTPS host)." });
            }

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

    [HttpPost("suppliers/{id:guid}/grant-founding-partner")]
    public async Task<IActionResult> GrantFoundingPartner(Guid id)
    {
        var supplier = await Db.Suppliers.FindAsync(id);
        if (supplier is null) return NotFound(Error("Supplier not found"));

        supplier.FoundingPartner = true;
        supplier.UpdatedAt       = DateTime.UtcNow;
        Audit("supplier.founding_partner_granted", User.GetUserEmail(),
            supplier.Name, "permanent status granted");
        await Db.SaveChangesAsync();

        return Ok(new { supplierId = supplier.Id, foundingPartner = true });
    }

    [HttpPost("suppliers/{id:guid}/revoke-founding-partner")]
    public async Task<IActionResult> RevokeFoundingPartner(Guid id)
    {
        var supplier = await Db.Suppliers.FindAsync(id);
        if (supplier is null) return NotFound(Error("Supplier not found"));

        supplier.FoundingPartner = false;
        supplier.UpdatedAt       = DateTime.UtcNow;
        Audit("supplier.founding_partner_revoked", User.GetUserEmail(),
            supplier.Name, "status revoked");
        await Db.SaveChangesAsync();

        return Ok(new { supplierId = supplier.Id, foundingPartner = false });
    }

    [HttpPost("suppliers/{id:guid}/verify")]
    public async Task<IActionResult> VerifySupplier(Guid id)
    {
        var supplier = await Db.Suppliers.FindAsync(id);
        if (supplier is null) return NotFound(Error("Supplier not found"));

        supplier.IsVerified = true;
        supplier.VerifiedAt = DateTime.UtcNow;
        supplier.UpdatedAt  = DateTime.UtcNow;
        Audit("supplier.verified", User.GetUserEmail(), supplier.Name, null);
        await Db.SaveChangesAsync();
        return Ok(new { supplierId = supplier.Id, isVerified = true, verifiedAt = supplier.VerifiedAt });
    }

    [HttpPost("suppliers/{id:guid}/unverify")]
    public async Task<IActionResult> UnverifySupplier(Guid id)
    {
        var supplier = await Db.Suppliers.FindAsync(id);
        if (supplier is null) return NotFound(Error("Supplier not found"));

        supplier.IsVerified = false;
        supplier.VerifiedAt = null;
        supplier.UpdatedAt  = DateTime.UtcNow;
        Audit("supplier.unverified", User.GetUserEmail(), supplier.Name, null);
        await Db.SaveChangesAsync();
        return Ok(new { supplierId = supplier.Id, isVerified = false });
    }

    [HttpPatch("suppliers/{id:guid}/priority")]
    public async Task<IActionResult> UpdatePriorityLevel(Guid id, [FromBody] UpdatePriorityRequest body)
    {
        var supplier = await Db.Suppliers.FindAsync(id);
        if (supplier is null) return NotFound(Error("Supplier not found"));

        if (!Enum.TryParse<PriorityLevel>(body.Level, ignoreCase: true, out var level))
            return BadRequest(Error("Invalid priority level. Use Standard, High, or Critical."));

        supplier.PriorityLevel = level;
        supplier.UpdatedAt     = DateTime.UtcNow;
        Audit("supplier.priority_changed", User.GetUserEmail(),
            supplier.Name, $"priorityLevel → {level}");
        await Db.SaveChangesAsync();
        return Ok(new { supplierId = supplier.Id, priorityLevel = level.ToString() });
    }

    [HttpPost("suppliers/{id:guid}/approve-application")]
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
        if (supplier.OnboardingStartedAt is null)
            supplier.OnboardingStartedAt = DateTime.UtcNow;
        user.SupplierId    = supplier.Id;
        user.Role          = UserRole.Provider;

        var actorName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "admin";
        Audit(
            "approve_supplier_application",
            actorName,
            $"Supplier:{supplier.Id}",
            $"Linked to user {user.Email}");
        await Db.SaveChangesAsync();

        return Ok(new { message = "Supplier approved and user linked as provider." });
    }

    /// <summary>
    /// Triggers an immediate API poll for the given supplier, bypassing the schedule.
    /// Only works for Api-type integrations.
    /// </summary>
    [HttpPost("suppliers/{id:guid}/sync")]
    public async Task<IActionResult> TriggerSync(Guid id)
    {
        var supplier = await Db.Suppliers.FindAsync(id);
        if (supplier is null) return NotFound(Error("Supplier not found."));
        if (supplier.IntegrationType != IntegrationType.Api)
            return BadRequest(Error(
                $"Manual sync is only available for API-type integrations. " +
                $"This supplier uses '{supplier.IntegrationType}'."));

        var result = await pollingService.PollSupplierAsync(id);
        return Ok(new
        {
            result.Success,
            result.Status,
            result.ErrorMessage,
            result.UnitsRefreshed,
            result.DurationMs,
        });
    }

    /// <summary>
    /// Returns recent poll log entries for the given supplier, newest first.
    /// </summary>
    [HttpGet("suppliers/{id:guid}/poll-log")]
    public async Task<IActionResult> GetPollLog(Guid id, [FromQuery] int limit = 20)
    {
        var logs = await Db.PollingLogs
            .Where(p => p.SupplierId == id)
            .OrderByDescending(p => p.Timestamp)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(p => new
            {
                p.Id,
                Timestamp      = p.Timestamp.ToString("o"),
                p.Status,
                p.ErrorMessage,
                p.UnitsRefreshed,
                p.DurationMs,
            })
            .ToListAsync();
        return Ok(logs);
    }

    // ── Per-supplier integration read/write ───────────────────────────────────

    [HttpGet("suppliers/{id:guid}/integration")]
    public async Task<IActionResult> GetIntegration(Guid id)
    {
        var supplier = await Db.Suppliers
            .Include(s => s.IntegrationSettings)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (supplier is null) return NotFound(Error("Supplier not found."));

        // Backfill IntegrationSettings if missing (same logic as AdminIntegrationsController)
        if (supplier.IntegrationSettings is null)
        {
            var settings = new IntegrationSettings
            {
                SupplierId          = id,
                ApprovalMode        = ApprovalMode.Auto,
                PostingMode         = (PostingMode)(int)supplier.IntegrationType,
                FallbackPostingMode = PostingMode.Email,
                IsActive            = true,
                UpdatedAt           = DateTime.UtcNow,
            };
            Db.IntegrationSettings.Add(settings);
            await Db.SaveChangesAsync();
            supplier.IntegrationSettings = settings;
        }

        return Ok(MapIntegration(supplier));
    }

    [HttpPatch("suppliers/{id:guid}/integration")]
    public async Task<IActionResult> UpdateIntegration(
        Guid id, [FromBody] UpdateSupplierIntegrationRequest body)
    {
        var supplier = await Db.Suppliers
            .Include(s => s.IntegrationSettings)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (supplier is null) return NotFound(Error("Supplier not found."));

        // Supplier fields
        if (body.IntegrationType is not null &&
            Enum.TryParse<IntegrationType>(body.IntegrationType, true, out var it))
            supplier.IntegrationType = it;

        if (body.RecipientEmail is not null) supplier.RecipientEmail = body.RecipientEmail;
        if (body.ApiEndpoint is not null)
        {
            if (!string.IsNullOrWhiteSpace(body.ApiEndpoint) && !OutboundEndpointValidator.IsAllowed(body.ApiEndpoint))
                return BadRequest(new { error = "Endpoint URL is not allowed (must be a public HTTPS host)." });
            supplier.ApiEndpoint    = body.ApiEndpoint;
        }
        if (body.ApiAuthType is not null)    supplier.ApiAuthType    = body.ApiAuthType;
        if (!string.IsNullOrWhiteSpace(body.ApiAuthToken))
            supplier.ApiAuthToken = tokenProtector.Protect(body.ApiAuthToken);

        // Polling
        if (body.PollingEnabled.HasValue)
        {
            supplier.PollingEnabled = body.PollingEnabled.Value;
            if (body.PollingEnabled.Value && supplier.NextPollAt == null)
                supplier.NextPollAt = DateTime.UtcNow;
        }
        if (body.PollingIntervalMinutes.HasValue &&
            new[] { 15, 30, 60, 360, 1440 }.Contains(body.PollingIntervalMinutes.Value))
            supplier.PollingIntervalMinutes = body.PollingIntervalMinutes.Value;

        supplier.UpdatedAt = DateTime.UtcNow;

        // IntegrationSettings
        if (supplier.IntegrationSettings is not null)
        {
            var s = supplier.IntegrationSettings;
            if (body.ApprovalMode is not null &&
                Enum.TryParse<ApprovalMode>(body.ApprovalMode, true, out var am))
                s.ApprovalMode = am;
            if (body.PostingMode is not null &&
                Enum.TryParse<PostingMode>(body.PostingMode, true, out var pm))
                s.PostingMode = pm;
            if (body.FallbackPostingMode is not null &&
                Enum.TryParse<PostingMode>(body.FallbackPostingMode, true, out var fpm))
                s.FallbackPostingMode = fpm;
            s.UpdatedAt = DateTime.UtcNow;
        }

        Audit("supplier.integration.updated", User.GetUserEmail(), supplier.Name, null);
        await Db.SaveChangesAsync();
        return Ok(MapIntegration(supplier));
    }

    // ── Contract template management ──────────────────────────────────────────

    [HttpGet("suppliers/{id:guid}/contracts")]
    public async Task<IActionResult> GetContractTemplates(
        Guid id,
        [FromQuery] bool includeInactive = false)
    {
        var templates = await Db.ContractTemplates
            .Where(t => t.SupplierId == id && (includeInactive || t.IsActive))
            .OrderByDescending(t => t.IsDefault)
            .ThenBy(t => t.Name)
            .ToListAsync();

        return Ok(templates.Select(t => new ContractTemplateDto(
            t.Id, t.Name, t.HtmlTemplate, t.IsActive, t.IsDefault,
            t.CreatedAt.ToString("o"), t.UpdatedAt.ToString("o"))));
    }

    [HttpPost("suppliers/{id:guid}/contracts")]
    public async Task<IActionResult> CreateContractTemplate(
        Guid id, [FromBody] CreateContractTemplateRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Name))
            return BadRequest(Error("Template name is required."));
        if (string.IsNullOrWhiteSpace(body.Html))
            return BadRequest(Error("Html is required."));
        const int MaxTemplateHtmlBytes = 512 * 1024;
        if (body.Html.Length > MaxTemplateHtmlBytes)
            return BadRequest(Error("Template HTML must be under 512 KB."));

        // Clear any existing default for this supplier before setting a new one.
        if (body.IsDefault == true)
            await Db.ContractTemplates
                .Where(t => t.SupplierId == id && t.IsDefault)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDefault, false));

        var template = new ContractTemplate
        {
            SupplierId   = id,
            Name         = body.Name.Trim(),
            HtmlTemplate = body.Html,
            IsDefault    = body.IsDefault ?? false,
        };
        Db.ContractTemplates.Add(template);
        await Db.SaveChangesAsync();

        return Created(
            $"/api/admin/suppliers/{id}/contracts/{template.Id}",
            new ContractTemplateDto(
                template.Id, template.Name, template.HtmlTemplate,
                template.IsActive, template.IsDefault,
                template.CreatedAt.ToString("o"), template.UpdatedAt.ToString("o")));
    }

    [HttpPatch("suppliers/{id:guid}/contracts/{templateId:guid}")]
    public async Task<IActionResult> UpdateContractTemplate(
        Guid id, Guid templateId, [FromBody] UpdateContractTemplateRequest body)
    {
        var template = await Db.ContractTemplates
            .FirstOrDefaultAsync(t => t.Id == templateId && t.SupplierId == id);
        if (template is null) return NotFound(Error("Contract template not found."));

        if (body.Name is not null) template.Name         = body.Name.Trim();
        if (body.Html is not null)
        {
            const int MaxTemplateHtmlBytes = 512 * 1024;
            if (body.Html.Length > MaxTemplateHtmlBytes)
                return BadRequest(Error("Template HTML must be under 512 KB."));
            template.HtmlTemplate = body.Html;
        }
        if (body.IsActive.HasValue)        template.IsActive     = body.IsActive.Value;
        if (body.IsDefault == true)
        {
            await Db.ContractTemplates
                .Where(t => t.SupplierId == id && t.IsDefault && t.Id != templateId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDefault, false));
            template.IsDefault = true;
        }
        else if (body.IsDefault == false)
        {
            template.IsDefault = false;
        }

        template.UpdatedAt = DateTime.UtcNow;
        await Db.SaveChangesAsync();

        return Ok(new ContractTemplateDto(
            template.Id, template.Name, template.HtmlTemplate,
            template.IsActive, template.IsDefault,
            template.CreatedAt.ToString("o"), template.UpdatedAt.ToString("o")));
    }

    [HttpDelete("suppliers/{id:guid}/contracts/{templateId:guid}")]
    public async Task<IActionResult> DeleteContractTemplate(Guid id, Guid templateId)
    {
        var template = await Db.ContractTemplates
            .FirstOrDefaultAsync(t => t.Id == templateId && t.SupplierId == id);
        if (template is null) return NotFound(Error("Contract template not found."));

        // Soft-delete: deactivate instead of hard delete.
        // Hard-deleting a template that has existing SignedContracts would leave
        // dangling ContractTemplateId references.
        template.IsActive  = false;
        template.IsDefault = false;   // clear default so a new template can be set
        template.UpdatedAt = DateTime.UtcNow;
        await Db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("suppliers/demo")]
    public async Task<IActionResult> DeleteDemoSuppliers()
    {
        var demoSuppliers = await Db.Suppliers
            .Where(s => s.Name.StartsWith("[Demo]"))
            .ToListAsync();

        var supplierIds = demoSuppliers.Select(s => s.Id).ToList();

        // Collect IDs needed for child-table deletes
        var locationIds = await Db.SupplierLocations
            .Where(l => supplierIds.Contains(l.SupplierId))
            .Select(l => l.Id).ToListAsync();

        var listingIds = await Db.Listings
            .Where(l => supplierIds.Contains(l.SupplierId))
            .Select(l => l.Id).ToListAsync();

        var bookingIds = listingIds.Count == 0 ? [] : await Db.Bookings
            .IgnoreQueryFilters()
            .Where(b => listingIds.Contains(b.ListingId))
            .Select(b => b.Id).ToListAsync();

        var orderIds = await Db.Orders
            .IgnoreQueryFilters()
            .Where(o => supplierIds.Contains(o.SupplierId))
            .Select(o => o.Id).ToListAsync();

        // Delete leaf → root
        if (locationIds.Count > 0)
            await Db.BlockedDates.Where(b => locationIds.Contains(b.LocationId)).ExecuteDeleteAsync();

        if (bookingIds.Count > 0)
        {
            await Db.SignedContracts.Where(sc => bookingIds.Contains(sc.BookingId)).ExecuteDeleteAsync();
            await Db.Messages.Where(m => bookingIds.Contains(m.BookingId)).ExecuteDeleteAsync();
            await Db.Reviews.Where(r => bookingIds.Contains(r.BookingId)).ExecuteDeleteAsync();
            await Db.Invoices.Where(i => bookingIds.Contains(i.BookingId)).ExecuteDeleteAsync();
            await Db.BookingTimelines.Where(bt => bookingIds.Contains(bt.BookingId)).ExecuteDeleteAsync();
        }

        if (orderIds.Count > 0)
        {
            await Db.FulfillmentEvents.Where(fe => orderIds.Contains(fe.OrderId)).ExecuteDeleteAsync();
            await Db.OrderTimelines.Where(ot => orderIds.Contains(ot.OrderId)).ExecuteDeleteAsync();
            await Db.Orders.IgnoreQueryFilters()
                .Where(o => orderIds.Contains(o.Id)).ExecuteDeleteAsync();
        }

        if (bookingIds.Count > 0)
            await Db.Bookings.IgnoreQueryFilters()
                .Where(b => bookingIds.Contains(b.Id)).ExecuteDeleteAsync();

        if (listingIds.Count > 0)
        {
            await Db.ListingExtras.Where(le => listingIds.Contains(le.ListingId)).ExecuteDeleteAsync();
            await Db.Listings.Where(l => listingIds.Contains(l.Id)).ExecuteDeleteAsync();
        }

        await Db.PayoutEntries.Where(pe => supplierIds.Contains(pe.SupplierId)).ExecuteDeleteAsync();
        await Db.RebateInvoices.Where(ri => supplierIds.Contains(ri.SupplierId)).ExecuteDeleteAsync();
        await Db.PollingLogs.Where(pl => supplierIds.Contains(pl.SupplierId)).ExecuteDeleteAsync();
        await Db.OrderRoutingRules
            .Where(rr => rr.SupplierId.HasValue && supplierIds.Contains(rr.SupplierId.Value))
            .ExecuteDeleteAsync();
        await Db.ContractTemplates.Where(ct => supplierIds.Contains(ct.SupplierId)).ExecuteDeleteAsync();
        await Db.SupplierLocations.Where(sl => supplierIds.Contains(sl.SupplierId)).ExecuteDeleteAsync();
        await Db.IntegrationSettings.Where(si => supplierIds.Contains(si.SupplierId)).ExecuteDeleteAsync();
        Db.Suppliers.RemoveRange(demoSuppliers);
        await Db.SaveChangesAsync();
        return Ok(new { removedSuppliers = demoSuppliers.Count });
    }

    [HttpPost("suppliers/{id:guid}/approve")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ApproveSupplier(Guid id)
    {
        var supplier = await Db.Suppliers.FindAsync(id);
        if (supplier is null) return NotFound(Error("Supplier not found."));

        if (supplier.IsActive)
            return Conflict(Error("Supplier is already active."));

        // Find the linked user
        var user = await Db.Users.FirstOrDefaultAsync(u => u.SupplierId == id);

        supplier.IsActive            = true;
        supplier.OnboardingStartedAt = DateTime.UtcNow;
        supplier.UpdatedAt           = DateTime.UtcNow;

        if (user is not null)
            user.Role = UserRole.Provider;

        Audit("supplier.approved", User.GetUserEmail() ?? "admin", supplier.Name, $"SupplierId: {id}");

        await Db.SaveChangesAsync();

        // Send welcome email
        if (user is not null)
        {
            var tl        = EmailTranslations.For(user.Language);
            var emailSender = HttpContext.RequestServices.GetRequiredService<IEmailSender>();
            emailSender.SendAsync(
                to:       supplier.ContactEmail,
                subject:  tl.SupplierWelcomeSubject,
                textBody: tl.SupplierWelcomeBody(supplier.ContactName)
            ).FireAndForget(logger, "supplier-welcome-email");
        }

        return Ok(new
        {
            supplierId = supplier.Id,
            name       = supplier.Name,
            isActive   = supplier.IsActive,
        });
    }

    // ── GET /api/admin/suppliers/{id}/activation-checklist ───────────────────

    /// <summary>
    /// Returns a readiness checklist for a supplier — how "launch-ready" they are.
    /// All items derived from existing DB data; no new columns needed.
    /// </summary>
    [HttpGet("suppliers/{id:guid}/activation-checklist")]
    public async Task<IActionResult> GetActivationChecklist(Guid id)
    {
        var supplier = await Db.Suppliers.FindAsync(id);
        if (supplier is null) return NotFound(Error("Supplier not found."));

        // Load active listings for this supplier (with image/price data)
        var activeListings = await Db.Listings
            .Where(l => l.SupplierId == id && l.IsActive)
            .Select(l => new { l.PriceFrom, l.ImagesJson })
            .ToListAsync();

        // Check each criterion independently
        var hasActiveListings = activeListings.Count > 0;

        // At least one listing has an image (ImagesJson != "[]" and != "{}")
        var hasImages = activeListings.Any(l =>
            !string.IsNullOrEmpty(l.ImagesJson) &&
            l.ImagesJson != "[]" && l.ImagesJson != "{}");

        // All active listings have a price > 0
        var hasPricing = activeListings.Count > 0 && activeListings.All(l => l.PriceFrom > 0);

        // Bank details on file (IBAN set)
        var hasBankDetails = !string.IsNullOrWhiteSpace(supplier.Iban);

        // Partner contract signed — at least one signed contract linked to a booking
        // for this supplier
        var hasSignedContract = await Db.SignedContracts
            .AnyAsync(sc => sc.Booking.SupplierId == id && sc.Status == "completed");

        // Admin-verified flag
        var isVerified = supplier.IsVerified;

        var checklist = new[]
        {
            new { key = "hasActiveListings", done = hasActiveListings,  label = "Has at least 1 active listing" },
            new { key = "hasImages",         done = hasImages,          label = "At least 1 listing has an image" },
            new { key = "hasPricing",        done = hasPricing,         label = "All listings have a price > 0" },
            new { key = "hasBankDetails",    done = hasBankDetails,     label = "Bank details (IBAN) on file" },
            new { key = "hasSignedContract", done = hasSignedContract,  label = "Partner contract signed" },
            new { key = "isVerified",        done = isVerified,         label = "Admin has verified the partner" },
        };

        var readinessScore = checklist.Count(c => c.done);
        var readinessTotal = checklist.Length;

        return Ok(new
        {
            supplierId     = supplier.Id,
            supplierName   = supplier.Name,
            checklist,
            readinessScore,
            readinessTotal,
        });
    }

    // ── Mappers ───────────────────────────────────────────────────────────────

    private static SupplierIntegrationDto MapIntegration(Supplier s)
    {
        var si = s.IntegrationSettings;
        return new SupplierIntegrationDto(
            IntegrationType:        s.IntegrationType.ToString(),
            ApiEndpoint:            s.ApiEndpoint,
            ApiAuthType:            s.ApiAuthType,
            HasApiToken:            !string.IsNullOrEmpty(s.ApiAuthToken),
            RecipientEmail:         s.RecipientEmail,
            IntegrationHealth:      s.IntegrationHealth.ToString(),
            IntegrationSettingsId:  si?.Id ?? Guid.Empty,
            ApprovalMode:           si?.ApprovalMode.ToString() ?? "Auto",
            PostingMode:            si?.PostingMode.ToString() ?? "Email",
            FallbackPostingMode:    si?.FallbackPostingMode.ToString() ?? "Email",
            PollingEnabled:         s.PollingEnabled,
            PollingIntervalMinutes: s.PollingIntervalMinutes,
            NextPollAt:             s.NextPollAt?.ToString("o"),
            LastPolledAt:           s.LastPolledAt?.ToString("o"),
            LastPollStatus:         s.LastPollStatus
        );
    }
}
