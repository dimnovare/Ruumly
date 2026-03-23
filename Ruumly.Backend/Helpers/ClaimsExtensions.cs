using System.Security.Claims;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Helpers;

public static class ClaimsExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? user.FindFirstValue("sub")
                 ?? throw new UnauthorizedAccessException("User ID claim missing");
        return Guid.Parse(value);
    }

    public static UserRole GetUserRole(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.Role)
                 ?? throw new UnauthorizedAccessException("Role claim missing");
        return Enum.Parse<UserRole>(value);
    }

    public static string GetUserEmail(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Email)
        ?? user.FindFirstValue("email")
        ?? throw new UnauthorizedAccessException("Email claim missing");
}
