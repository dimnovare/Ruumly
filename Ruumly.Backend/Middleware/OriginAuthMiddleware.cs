using System.Security.Cryptography;
using System.Text;

namespace Ruumly.Backend.Middleware;

/// <summary>
/// Optional origin lock (defence-in-depth). When <c>Security:OriginSecret</c>
/// (env <c>SECURITY__ORIGINSECRET</c>) is configured, every request must carry a
/// matching <c>X-Origin-Auth</c> header — injected by a Cloudflare Transform Rule
/// on the zone — otherwise it is rejected with 403. This blocks direct-to-origin
/// access that bypasses Cloudflare (e.g. the public *.up.railway.app domain),
/// which is the Railway-compatible alternative to Authenticated Origin Pulls
/// (Railway's managed TLS can't enforce a client cert at the origin).
///
/// OFF BY DEFAULT: if the secret is empty the middleware is a pure no-op, so it
/// ships safely and is enabled later by setting the env var AFTER the Cloudflare
/// rule is confirmed live (set the rule first, verify, then set the secret).
///
/// The Railway health-check path (<c>/health</c>) is ALWAYS allowed: Railway
/// probes the origin directly, bypassing Cloudflare, so that request never
/// carries the header. Blocking it would fail the deploy's health check.
/// </summary>
public class OriginAuthMiddleware(
    RequestDelegate next,
    IConfiguration config,
    ILogger<OriginAuthMiddleware> logger)
{
    private const string HeaderName = "X-Origin-Auth";
    private readonly byte[]? _secret = ReadSecret(config);

    private static byte[]? ReadSecret(IConfiguration config)
    {
        var s = config["Security:OriginSecret"];
        return string.IsNullOrWhiteSpace(s) ? null : Encoding.UTF8.GetBytes(s);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // No-op unless an origin secret is configured.
        if (_secret is null)
        {
            await next(context);
            return;
        }

        // Railway health probe hits the origin directly (no Cloudflare) — always allow.
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        var provided = context.Request.Headers[HeaderName].FirstOrDefault();
        if (provided is not null)
        {
            var providedBytes = Encoding.UTF8.GetBytes(provided);
            if (providedBytes.Length == _secret.Length
                && CryptographicOperations.FixedTimeEquals(providedBytes, _secret))
            {
                await next(context);
                return;
            }
        }

        logger.LogWarning(
            "Rejected request missing/invalid {Header} (direct-to-origin?): {Method} {Path}",
            HeaderName, context.Request.Method, context.Request.Path);
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = "Forbidden" });
    }
}
