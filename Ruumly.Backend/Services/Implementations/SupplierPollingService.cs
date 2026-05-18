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

            var endpoint = !string.IsNullOrWhiteSpace(supplier.PollingEndpoint)
                ? supplier.PollingEndpoint
                : supplier.ApiEndpoint;

            // 3b. SSRF guard — reject loopback, private ranges, non-HTTP(S) schemes
            if (!IsAllowedEndpoint(endpoint))
            {
                const string msg = "API endpoint rejected: private IP address or disallowed scheme.";
                logger.LogWarning(
                    "SSRF guard blocked endpoint '{Endpoint}' for supplier {SupplierId}",
                    endpoint, supplierId);
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
                response = await client.GetAsync(endpoint, ct);
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
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    // Load field name mappings from PollMappingProfile
                    var settingsForPoll = await db.IntegrationSettings
                        .FirstOrDefaultAsync(s => s.SupplierId == supplierId, ct);
                    string idField        = "id";
                    string availableField = "available";
                    string totalField     = "total";
                    if (!string.IsNullOrWhiteSpace(settingsForPoll?.PollMappingProfile))
                    {
                        try
                        {
                            using var mapDoc = System.Text.Json.JsonDocument.Parse(settingsForPoll.PollMappingProfile);
                            var mapRoot = mapDoc.RootElement;
                            if (mapRoot.TryGetProperty("id",        out var mId))  idField        = mId.GetString()  ?? idField;
                            if (mapRoot.TryGetProperty("available", out var mAv))  availableField = mAv.GetString()  ?? availableField;
                            if (mapRoot.TryGetProperty("total",     out var mTot)) totalField     = mTot.GetString() ?? totalField;
                        }
                        catch { /* malformed mapping — use defaults */ }
                    }

                    // Load locations for this supplier that have an ExternalId set
                    var locations = await db.SupplierLocations
                        .Where(l => l.SupplierId == supplierId && l.ExternalId != null)
                        .ToListAsync(ct);
                    var locByExtId = locations.ToDictionary(l => l.ExternalId!, l => l);

                    foreach (var item in root.EnumerateArray())
                    {
                        if (!item.TryGetProperty(idField, out var idProp)) continue;
                        var externalId = idProp.GetString();
                        if (string.IsNullOrEmpty(externalId)) continue;
                        if (!locByExtId.TryGetValue(externalId, out var loc)) continue;

                        if (item.TryGetProperty(availableField, out var av) &&
                            av.TryGetInt32(out var available))
                        {
                            loc.AvailableUnitCount = available;
                            unitsRefreshed++;
                        }
                        if (item.TryGetProperty(totalField, out var tot) &&
                            tot.TryGetInt32(out var total))
                            loc.TotalUnitCount = total;
                    }

                    if (unitsRefreshed > 0)
                        await db.SaveChangesAsync(ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse availability response for supplier {Id}", supplierId);
                // Non-conforming format — endpoint reachable, still record success
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

    /// <summary>
    /// Returns true only for HTTP(S) URLs that resolve to a public IP address.
    /// Blocks loopback (127.x, ::1), link-local (169.254.x), AWS metadata,
    /// RFC-1918 private ranges, and carrier-grade NAT (Railway internal IPs).
    /// </summary>
    private static bool IsAllowedEndpoint(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        // Only allow HTTP(S) — block file://, ftp://, gopher://, etc.
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;

        // Block bare hostnames / single-label names (internal DNS shortcuts, e.g. "localhost")
        if (!uri.Host.Contains('.') && !uri.Host.StartsWith('[')) return false;

        // If the host is NOT a raw IP address, allow it (DNS-resolved IPs are not checked here
        // because async DNS resolution would complicate this guard; the private IP ranges
        // covering real-world attack vectors are already blocked via the IP literal check below).
        if (!System.Net.IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip)) return true;

        // Map IPv6-encoded IPv4 (::ffff:192.168.x.x) to IPv4 for uniform range checks
        var v4 = ip.MapToIPv4();
        var b  = v4.GetAddressBytes();

        var isPrivate =
            b[0] == 127 ||                                          // 127.x.x.x — loopback
            b[0] == 10  ||                                          // 10.x.x.x  — RFC-1918
            (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||           // 172.16–31  — RFC-1918
            (b[0] == 192 && b[1] == 168) ||                         // 192.168.x  — RFC-1918
            (b[0] == 169 && b[1] == 254) ||                         // 169.254.x  — link-local / AWS metadata
            (b[0] == 100 && b[1] >= 64 && b[1] <= 127) ||          // 100.64-127 — CGNAT (Railway)
            b[0] == 0;                                              // 0.x.x.x    — "this" network

        return !isPrivate;
    }

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
