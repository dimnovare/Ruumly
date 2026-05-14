using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class SupplierPollingService(
    RuumlyDbContext                     db,
    IHttpClientFactory                  httpFactory,
    TokenProtector                      tokenProtector,
    ILogger<SupplierPollingService>     logger) : ISupplierPollingService
{
    public async Task<PollingResult> PollSupplierAsync(
        Guid supplierId, CancellationToken ct = default)
    {
        try
        {
            // 1. Load supplier
            var supplier = await db.Suppliers
                .FirstOrDefaultAsync(s => s.Id == supplierId, ct);

            if (supplier is null || !supplier.IsActive)
                return Skipped(supplierId, "Supplier not found or inactive.");

            // 2. Only API-type integrations have an endpoint to call
            if (supplier.IntegrationType != IntegrationType.Api)
            {
                var msg = $"Integration type '{supplier.IntegrationType}' does not support polling.";
                return await WriteLogAndReturn(
                    db, supplierId, new PollingResult(true, "skipped", msg, 0, 0), ct);
            }

            // 3. Endpoint must be configured
            if (string.IsNullOrWhiteSpace(supplier.ApiEndpoint))
            {
                const string msg = "No API endpoint configured.";
                supplier.LastPollStatus    = "error";
                supplier.LastPolledAt      = DateTime.UtcNow;
                supplier.IntegrationHealth = IntegrationHealth.Degraded;
                await db.SaveChangesAsync(ct);
                return await WriteLogAndReturn(
                    db, supplierId, new PollingResult(false, "error", msg, 0, 0), ct);
            }

            // 4. Build HTTP request
            var sw = Stopwatch.StartNew();
            using var client = httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            if (!string.IsNullOrWhiteSpace(supplier.ApiAuthType) &&
                !string.IsNullOrWhiteSpace(supplier.ApiAuthToken))
            {
                var plain = tokenProtector.Unprotect(supplier.ApiAuthToken);
                if (!string.IsNullOrWhiteSpace(plain))
                {
                    var scheme = supplier.ApiAuthType.Equals("Bearer",
                        StringComparison.OrdinalIgnoreCase) ? "Bearer" : supplier.ApiAuthType;
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue(scheme, plain);
                }
            }

            // 5. Execute GET
            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync(supplier.ApiEndpoint, ct);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var errMsg = $"HTTP request failed: {ex.Message}";
                supplier.LastPolledAt      = DateTime.UtcNow;
                supplier.LastPollStatus    = "error";
                supplier.NextPollAt        = DateTime.UtcNow.AddMinutes(supplier.PollingIntervalMinutes);
                supplier.IntegrationHealth = IntegrationHealth.Degraded;
                await db.SaveChangesAsync(ct);
                var result = new PollingResult(false, "error", errMsg, 0, (int)sw.ElapsedMilliseconds);
                return await WriteLogAndReturn(db, supplierId, result, ct);
            }

            if (!response.IsSuccessStatusCode)
            {
                sw.Stop();
                var body   = await response.Content.ReadAsStringAsync(ct);
                var errMsg = $"HTTP {(int)response.StatusCode}: {body[..Math.Min(200, body.Length)]}";
                supplier.LastPolledAt      = DateTime.UtcNow;
                supplier.LastPollStatus    = "error";
                supplier.NextPollAt        = DateTime.UtcNow.AddMinutes(supplier.PollingIntervalMinutes);
                supplier.IntegrationHealth = IntegrationHealth.Degraded;
                await db.SaveChangesAsync(ct);
                var result = new PollingResult(false, "error", errMsg, 0, (int)sw.ElapsedMilliseconds);
                return await WriteLogAndReturn(db, supplierId, result, ct);
            }

            // 6. Parse response — generic availability extraction
            var json = await response.Content.ReadAsStringAsync(ct);
            logger.LogDebug("Poll response from {Supplier}: {Json}", supplier.Name,
                json.Length > 500 ? json[..500] + "…" : json);

            int unitsRefreshed = 0;
            try
            {
                using var doc  = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                    {
                        if (!item.TryGetProperty("id", out var idProp)) continue;
                        var externalId = idProp.GetString();
                        if (string.IsNullOrEmpty(externalId)) continue;

                        if (!item.TryGetProperty("available", out _)) continue;
                        // TODO: add Listing.ExternalId in a follow-up migration to enable
                        // per-listing availability updates from this payload.
                        unitsRefreshed++;
                    }
                }
            }
            catch
            {
                // Non-conforming format — endpoint is reachable, just record success.
            }

            sw.Stop();

            // 7. Update supplier stats
            supplier.LastPolledAt      = DateTime.UtcNow;
            supplier.LastPollStatus    = "ok";
            supplier.NextPollAt        = DateTime.UtcNow.AddMinutes(supplier.PollingIntervalMinutes);
            supplier.IntegrationHealth = IntegrationHealth.Healthy;
            await db.SaveChangesAsync(ct);

            // 8. Write log + return
            var successResult = new PollingResult(true, "success", null, unitsRefreshed, (int)sw.ElapsedMilliseconds);
            return await WriteLogAndReturn(db, supplierId, successResult, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception polling supplier {SupplierId}", supplierId);
            var errResult = new PollingResult(false, "error", ex.Message, 0, 0);

            // Best-effort: update supplier status and write log
            try
            {
                var s = await db.Suppliers.FindAsync([supplierId], ct);
                if (s is not null)
                {
                    s.LastPollStatus    = "error";
                    s.LastPolledAt      = DateTime.UtcNow;
                    s.IntegrationHealth = IntegrationHealth.Degraded;
                    await db.SaveChangesAsync(ct);
                }
                await WriteLogAndReturn(db, supplierId, errResult, ct);
            }
            catch { /* swallow — don't mask the original exception path */ }

            return errResult;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static PollingResult Skipped(Guid supplierId, string reason) =>
        new(true, "skipped", reason, 0, 0);

    private static async Task<PollingResult> WriteLogAndReturn(
        RuumlyDbContext db, Guid supplierId, PollingResult result, CancellationToken ct)
    {
        db.PollingLogs.Add(new PollingLog
        {
            SupplierId     = supplierId,
            Timestamp      = DateTime.UtcNow,
            Status         = result.Status,
            ErrorMessage   = result.ErrorMessage,
            UnitsRefreshed = result.UnitsRefreshed,
            DurationMs     = result.DurationMs,
        });
        await db.SaveChangesAsync(ct);
        return result;
    }
}
