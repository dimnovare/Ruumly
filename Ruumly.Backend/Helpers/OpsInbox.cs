using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// Single source of truth for the internal ops/alerts inbox that receives
/// every concierge-side notification (concierge intake, routed-quote leads,
/// disputes, offer-chosen). Reads the <c>opsInbox</c> PlatformSettings key
/// (key/value table — no migration) and falls back to <see cref="Fallback"/>.
/// Replaces the scattered "admin@ruumly.eu" / "info@ruumly.eu" literals so a
/// founder can repoint every ops alert from one place.
/// </summary>
public static class OpsInbox
{
    /// <summary>Default inbox when the <c>opsInbox</c> setting is unset/blank.</summary>
    public const string Fallback = "info@ruumly.eu";

    public static async Task<string> ResolveAsync(RuumlyDbContext db)
    {
        var configured = await db.PlatformSettings
            .Where(s => s.Key == "opsInbox")
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        return string.IsNullOrWhiteSpace(configured) ? Fallback : configured.Trim();
    }
}
