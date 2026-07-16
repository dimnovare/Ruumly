using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// The SPA's CORS policy. Extracted from Program.cs so the origin allow-list and
/// the exposed-header set are unit-testable — this is the exact code Program.cs
/// runs, not a copy of it.
/// </summary>
public static class CorsPolicySetup
{
    public const string PolicyName = "Frontend";

    /// <summary>
    /// Response headers the browser must reveal to SPA JavaScript.
    ///
    /// Cross-origin JS can only read the CORS-safelisted set (Cache-Control,
    /// Content-Language, Content-Length, Content-Type, Expires, Last-Modified,
    /// Pragma). api.ruumly.eu and ruumly.eu ARE different origins, so every other
    /// header we return is invisible to fetch() unless it is listed here —
    /// silently, with no error.
    ///
    /// Retry-After: the 429 backoff the rate limiter sets (Program.cs OnRejected).
    /// Without exposing it the SPA cannot show a countdown, only a generic
    /// "try again later".
    /// </summary>
    private static readonly string[] ExposedHeaders = ["Retry-After"];

    public static void AddFrontendPolicy(CorsOptions options, string[] allowedOrigins) =>
        options.AddPolicy(PolicyName, policy => policy
            .SetIsOriginAllowed(origin => IsOriginAllowed(origin, allowedOrigins))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .WithExposedHeaders(ExposedHeaders));

    internal static bool IsOriginAllowed(string origin, string[] allowedOrigins)
    {
        if (allowedOrigins.Contains(origin)) return true;

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        if (!uri.Host.EndsWith(".vercel.app")) return false;

        // Only allow exact Ruumly Vercel project slugs.
        // Preview URLs follow the pattern: {projectName}-{hash}-{teamSlug}.vercel.app
        // Using a broad prefix like "ruumly-" would let any attacker register
        // "ruumly-evil.vercel.app" and make credentialed cross-origin requests.
        var host = uri.Host;
        return host.StartsWith("estonia-space-hub-") ||
               host == "estonia-space-hub.vercel.app";
        // Add future project slugs explicitly — never use a short prefix alone.
    }
}
