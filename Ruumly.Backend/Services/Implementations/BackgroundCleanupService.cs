using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;

namespace Ruumly.Backend.Services.Implementations;

public class BackgroundCleanupService(
    RuumlyDbContext db,
    ILogger<BackgroundCleanupService> logger)
{
    /// <summary>
    /// Deletes refresh tokens that are revoked or expired, provided they were
    /// created more than 7 days ago (grace period to avoid racing active sessions).
    /// Registered as a Hangfire recurring job (daily).
    /// </summary>
    public async Task CleanupStaleRefreshTokensAsync()
    {
        var ageCutoff = DateTime.UtcNow.AddDays(-7);
        var now       = DateTime.UtcNow;

        var deleted = await db.RefreshTokens
            .Where(t => (t.IsRevoked || t.ExpiresAt < now) && t.CreatedAt < ageCutoff)
            .ExecuteDeleteAsync();

        logger.LogInformation(
            "CleanupStaleRefreshTokens: deleted {Count} stale refresh token(s)", deleted);
    }
}
