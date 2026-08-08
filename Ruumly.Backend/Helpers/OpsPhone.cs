using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// Support phone printed under the signature of outbound provider mail. Reads
/// the <c>opsPhone</c> PlatformSettings key (key/value table — no migration).
/// Returns null when unset: callers must then omit the phone line entirely
/// rather than render an empty or placeholder number.
/// </summary>
public static class OpsPhone
{
    public static async Task<string?> ResolveAsync(RuumlyDbContext db, CancellationToken ct = default)
    {
        var configured = await db.PlatformSettings
            .Where(s => s.Key == "opsPhone")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(configured) ? null : configured.Trim();
    }
}
