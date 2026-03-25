namespace Ruumly.Backend.Middleware;

/// <summary>
/// Transparently rewrites unversioned API calls (e.g. /api/listings)
/// to the v1 versioned path (e.g. /api/v1/listings) so the existing
/// frontend continues to work without any changes.
///
/// Pattern: /api/{anything} where the segment after /api/ is NOT already
/// a version token (v1, v2, …).
/// </summary>
public sealed class ApiVersionRewriteMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Only rewrite paths under /api/ that don't already carry a version segment.
        // A versioned path looks like /api/v1/... or /api/v2/... etc.
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            && !IsAlreadyVersioned(path))
        {
            // /api/foo  →  /api/v1/foo
            context.Request.Path = "/api/v1" + path[4..];
        }

        return next(context);
    }

    private static bool IsAlreadyVersioned(string path)
    {
        // Matches /api/v<digits>/ — e.g. /api/v1/, /api/v2/, /api/v10/
        var segment = path.AsSpan(5); // skip "/api/"
        if (segment.Length < 2 || segment[0] != 'v') return false;

        int i = 1;
        while (i < segment.Length && char.IsDigit(segment[i])) i++;

        // Must have at least one digit and be followed by '/' or be end-of-string
        return i > 1 && (i == segment.Length || segment[i] == '/');
    }
}
