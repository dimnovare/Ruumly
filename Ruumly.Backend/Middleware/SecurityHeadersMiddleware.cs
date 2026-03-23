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

        if (!context.Request.IsHttps && context.Request.Host.Host != "localhost")
            context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

        await next(context);
    }
}
