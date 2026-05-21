using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Data;
using Ruumly.Backend.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/settings")]
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
        // Whitelist of exact keys safe to expose publicly
        var publicKeys = new[]
        {
            "siteName",
            "siteEmail",
            "sitePhone",
            "openHours",
            "openHoursSat",
            "inviteCodeRequired",
            "maintenanceMode",
            "showMovingService",
            "showTrailerService",
            "showFeaturedListings",
            "showHowItWorks",
            "showProviderCta",
            "showFaq",
            "showMap",
            "heroSubtitle",
            "heroDiscount",
            "defaultLanguage",
            "currency",
            "featuredPartners",
            // Note: "inviteCode" is NOT included — the actual code stays server-side only
        };

        // Prefixes whose keys are also public (e.g. aboutPage.enabled, aboutPage.mission.en)
        var publicPrefixes = new[] { "aboutPage.", "blog." };

        var settings = await db.PlatformSettings
            .Where(s => publicKeys.Contains(s.Key)
                     || publicPrefixes.Any(p => s.Key.StartsWith(p)))
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        // featuredPartners is stored as a JSON string in the DB; parse it here so
        // the frontend can use Array.isArray() directly without a JSON.parse.
        List<object> featuredPartners;
        try
        {
            featuredPartners = JsonSerializer.Deserialize<List<object>>(
                settings.GetValueOrDefault("featuredPartners", "[]")) ?? [];
        }
        catch
        {
            featuredPartners = [];
        }

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
            showMovingService    = settings.GetValueOrDefault("showMovingService",    "true")  == "true",
            showTrailerService   = settings.GetValueOrDefault("showTrailerService",   "true")  == "true",
            showFeaturedListings = settings.GetValueOrDefault("showFeaturedListings", "true")  == "true",
            showHowItWorks       = settings.GetValueOrDefault("showHowItWorks",       "true")  == "true",
            showProviderCta      = settings.GetValueOrDefault("showProviderCta",      "true")  == "true",
            showFaq              = settings.GetValueOrDefault("showFaq",              "true")  == "true",
            showMap              = settings.GetValueOrDefault("showMap",              "true")  == "true",
            heroSubtitle         = settings.GetValueOrDefault("heroSubtitle",    ""),
            heroDiscount         = settings.GetValueOrDefault("heroDiscount",    "10"),
            defaultLanguage      = settings.GetValueOrDefault("defaultLanguage", "et"),
            currency             = settings.GetValueOrDefault("currency",        "EUR"),
            featuredPartners,
            // About page — pass all aboutPage.* keys as a sub-dictionary
            aboutPage = settings
                .Where(kv => kv.Key.StartsWith("aboutPage."))
                .ToDictionary(kv => kv.Key["aboutPage.".Length..], kv => kv.Value),
            // Blog settings
            blog = settings
                .Where(kv => kv.Key.StartsWith("blog."))
                .ToDictionary(kv => kv.Key["blog.".Length..], kv => kv.Value),
        });
    }

    [HttpGet("pricing")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPricingConfig(
        [FromServices] IPricingConfigService pricing)
    {
        var c = await pricing.GetAsync();
        return Ok(new
        {
            defaultPartnerDiscount = c.DefaultPartnerDiscountRate,
            ruumlyMinMarginRate    = c.RuumlyMinMarginRate,
            extrasMarginRate       = c.ExtrasMarginRate,
            defaultVatRate         = c.DefaultVatRate,
            tiers = new
            {
                starter  = new { c.Starter.CustomerDiscountRate,  c.Starter.MonthlyFee,  commissionRate = (int)c.Starter.CommissionRate  },
                standard = new { c.Standard.CustomerDiscountRate, c.Standard.MonthlyFee, commissionRate = (int)c.Standard.CommissionRate },
                premium  = new { c.Premium.CustomerDiscountRate,  c.Premium.MonthlyFee,  commissionRate = (int)c.Premium.CommissionRate  },
            },
        });
    }
}
