using Hangfire.Dashboard;

namespace Ruumly.Backend.Middleware;

/// <summary>
/// Restricts Hangfire dashboard access to authenticated Admin users only.
/// </summary>
public sealed class HangfireAdminAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();
        return http.User.Identity?.IsAuthenticated == true
            && http.User.IsInRole("Admin");
    }
}
