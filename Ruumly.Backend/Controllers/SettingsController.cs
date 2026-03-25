using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Ruumly.Backend.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/settings")]
public class SettingsController(RuumlyDbContext db) : ControllerBase
{
    /// <summary>
    /// Returns the non-sensitive public settings that are
    /// safe to expose without authentication — used by the
    /// homepage, footer, and contact page.
    /// </summary>
    [HttpGet("public")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPublic()
    {
        // Whitelist of keys safe to expose publicly
        var publicKeys = new[]
        {
            "siteName",
            "siteEmail",
            "sitePhone",
            "openHours",
            "openHoursSat",
            "inviteCodeRequired",
            "maintenanceMode",
            "showFeaturedListings",
            "showHowItWorks",
            "showProviderCta",
            "showFaq",
            "showMap",
            // Note: "inviteCode" is NOT included — the actual code stays server-side only
        };

        var settings = await db.PlatformSettings
            .Where(s => publicKeys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        // Provide defaults for any missing keys
        return Ok(new
        {
            siteName           = settings.GetValueOrDefault("siteName",           "Ruumly"),
            siteEmail          = settings.GetValueOrDefault("siteEmail",          "info@ruumly.eu"),
            sitePhone          = settings.GetValueOrDefault("sitePhone",          "+372 5555 1234"),
            openHours          = settings.GetValueOrDefault("openHours",          "E–R 9–18"),
            openHoursSat       = settings.GetValueOrDefault("openHoursSat",       ""),
            inviteCodeRequired = settings.GetValueOrDefault("inviteCodeRequired", "false") == "true",
            maintenanceMode      = settings.GetValueOrDefault("maintenanceMode",      "false") == "true",
            showFeaturedListings = settings.GetValueOrDefault("showFeaturedListings", "true")  == "true",
            showHowItWorks       = settings.GetValueOrDefault("showHowItWorks",       "true")  == "true",
            showProviderCta      = settings.GetValueOrDefault("showProviderCta",      "true")  == "true",
            showFaq              = settings.GetValueOrDefault("showFaq",              "true")  == "true",
            showMap              = settings.GetValueOrDefault("showMap",              "true")  == "true",
        });
    }
}
