namespace Ruumly.Backend.Middleware;

public class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"]        = "DENY";
        context.Response.Headers["X-XSS-Protection"]       = "1; mode=block";
        context.Response.Headers["Referrer-Policy"]        = "strict-origin-when-cross-origin";
        context.Response.Headers["Permissions-Policy"]     = "camera=(), microphone=(), geolocation=()";

        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' " +
                "https://accounts.google.com " +
                "https://www.googletagmanager.com; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data: https: blob:; " +
            "font-src 'self' data:; " +
            "connect-src 'self' " +
                "https://api.ruumly.eu " +
                "https://api.montonio.com " +
                "https://accounts.google.com " +
                "https://*.ingest.sentry.io " +
                "https://*.google-analytics.com " +
                "https://*.analytics.google.com " +
                "https://www.googletagmanager.com; " +
            "frame-src https://accounts.google.com; " +
            "object-src 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self';";

        if (!context.Request.IsHttps && context.Request.Host.Host != "localhost")
            context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

        await next(context);
    }
}
