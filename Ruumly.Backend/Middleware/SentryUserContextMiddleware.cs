using Sentry;
using System.Security.Claims;

namespace Ruumly.Backend.Middleware;

/// <summary>
/// Attaches the authenticated user's ID, email, and role to every Sentry
/// event so errors can be filtered/searched by user in the Sentry dashboard.
/// Runs after UseAuthentication so ClaimsPrincipal is already populated.
/// </summary>
public sealed class SentryUserContextMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            SentrySdk.ConfigureScope(scope =>
            {
                scope.User = new SentryUser
                {
                    Id       = user.FindFirstValue(ClaimTypes.NameIdentifier),
                    Email    = user.FindFirstValue(ClaimTypes.Email),
                };

                var role = user.FindFirstValue(ClaimTypes.Role);
                if (role is not null)
                    scope.SetTag("user.role", role);
            });
        }

        return next(context);
    }
}
