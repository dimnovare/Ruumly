using Sentry;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Ruumly.Backend.Middleware;

/// <summary>
/// Attaches the authenticated user's ID and role to every Sentry event so errors
/// can be filtered/searched by user in the Sentry dashboard.
/// Runs after UseAuthentication so ClaimsPrincipal is already populated.
///
/// GDPR note: raw email is NOT sent to Sentry. Instead, a 12-character hex prefix
/// of the SHA-256 hash is stored in the Username field. This is enough to correlate
/// events for the same user across a session without transmitting PII to a third-
/// party data processor. The full email is never recoverable from the hash prefix.
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
                var email = user.FindFirstValue(ClaimTypes.Email);

                scope.User = new SentryUser
                {
                    Id       = user.FindFirstValue(ClaimTypes.NameIdentifier),
                    // 12 hex chars (48 bits) of SHA-256 — correlatable but not reversible.
                    // Do NOT set Email — that would transmit raw PII to Sentry's servers.
                    Username = email is not null
                        ? Convert.ToHexString(
                            SHA256.HashData(Encoding.UTF8.GetBytes(email)))[..12]
                        : null,
                };

                var role = user.FindFirstValue(ClaimTypes.Role);
                if (role is not null)
                    scope.SetTag("user.role", role);
            });
        }

        return next(context);
    }
}
