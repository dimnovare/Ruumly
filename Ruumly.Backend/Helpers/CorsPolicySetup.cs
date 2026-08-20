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

    public static void AddFrontendPolicy(CorsOptions options, string[] allowedOrigins, string? vercelTeamSlug = null) =>
        options.AddPolicy(PolicyName, policy => policy
            .SetIsOriginAllowed(origin => IsOriginAllowed(origin, allowedOrigins, vercelTeamSlug))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .WithExposedHeaders(ExposedHeaders));

    internal static bool IsOriginAllowed(string origin, string[] allowedOrigins, string? vercelTeamSlug = null)
    {
        if (allowedOrigins.Contains(origin)) return true;

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        if (!uri.Host.EndsWith(".vercel.app")) return false;

        var host = uri.Host;

        // The project's OWN production alias is a fixed, globally-unique host that
        // only this project can own — safe to match exactly.
        if (host == "estonia-space-hub.vercel.app") return true;

        // The OLD rule matched any host STARTING WITH `estonia-space-hub-`, which
        // also matches `estonia-space-hub-evil.vercel.app` — a project ANYONE can
        // register, giving an attacker a credentialed cross-origin foothold (the
        // paired /auth/refresh CSRF skip trusts this very allow-list).
        //
        // It was ALSO wrong about the shape, which is why nobody noticed. Real
        // deployment hosts, observed via `vercel ls` on 2026-08-20:
        //     estonia-space-e0ijg6mxw-dimnovare-9994s-projects.vercel.app
        // Vercel TRUNCATES the project name, so `estonia-space-hub-` matched none
        // of our own deployments. The rule was simultaneously unsafe against
        // attackers and ineffective for us.
        //
        // What an attacker cannot forge is the TRAILING TEAM SLUG: Vercel issues
        // `-{ourSlug}.vercel.app` only to deployments inside our own team. That
        // suffix is the real security boundary. The project prefix is kept as a
        // cheap narrowing but is deliberately NOT load-bearing — every project
        // under our team slug is ours anyway — and it is matched against the
        // truncated form so it actually fires.
        //
        // FAIL CLOSED: with no slug configured, NO wildcard preview host is
        // trusted and only the explicit AllowedOrigins list applies. Set
        // `Cors:VercelTeamSlug` (env `Cors__VercelTeamSlug`) to enable it.
        if (string.IsNullOrWhiteSpace(vercelTeamSlug)) return false;

        return host.StartsWith("estonia-space", StringComparison.Ordinal) &&
               host.EndsWith($"-{vercelTeamSlug}.vercel.app", StringComparison.Ordinal);
    }
}
