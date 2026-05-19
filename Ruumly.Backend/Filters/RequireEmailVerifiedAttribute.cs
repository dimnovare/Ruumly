using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;

namespace Ruumly.Backend.Filters;

[AttributeUsage(AttributeTargets.Method)]
public class RequireEmailVerifiedAttribute : TypeFilterAttribute
{
    public RequireEmailVerifiedAttribute() : base(typeof(RequireEmailVerifiedFilter)) { }
}

public class RequireEmailVerifiedFilter(RuumlyDbContext db) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var userId = context.HttpContext.User.GetUserId();
        var user   = await db.Users.FindAsync(userId);

        if (user is null || !user.EmailVerified)
        {
            var msg = ErrorMessages.Get("EMAIL_NOT_VERIFIED", user?.Language ?? "en");
            context.Result = new ObjectResult(new { error = "EMAIL_NOT_VERIFIED", message = msg })
            {
                StatusCode = 403
            };
            return;
        }

        await next();
    }
}
