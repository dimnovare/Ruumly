using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using System.Globalization;
using System.Text;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("")]
[AllowAnonymous]
public class SitemapController(RuumlyDbContext db) : ControllerBase
{
    private const string BaseUrl = "https://ruumly.eu";
    private static readonly string[] Langs = ["et", "en", "ru", "lv", "lt"];

    // Emits five <url> entries (one per language prefix) for a given path.
    // path "" → {BaseUrl}/{lang} (no trailing slash) for the homepage.
    // path "/search" → {BaseUrl}/{lang}/search, etc.
    // x-default always points to the /et/ version.
    private static void AppendLangUrlSet(
        StringBuilder sb, string path, string priority, string changefreq, string? lastMod = null)
    {
        foreach (var lang in Langs)
        {
            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{BaseUrl}/{lang}{path}</loc>");
            if (lastMod is not null)
                sb.AppendLine($"    <lastmod>{lastMod}</lastmod>");
            sb.AppendLine($"    <changefreq>{changefreq}</changefreq>");
            sb.AppendLine($"    <priority>{priority}</priority>");

            foreach (var altLang in Langs)
                sb.AppendLine($"    <xhtml:link rel=\"alternate\" hreflang=\"{altLang}\" href=\"{BaseUrl}/{altLang}{path}\"/>");

            sb.AppendLine($"    <xhtml:link rel=\"alternate\" hreflang=\"x-default\" href=\"{BaseUrl}/et{path}\"/>");
            sb.AppendLine("  </url>");
        }
    }

    // Turns a raw city name into the ASCII slug used by the frontend CityPage
    // routes (/{lang}/storage/<slug>): strip combining diacritics, lower-case,
    // collapse whitespace runs to single hyphens. "Pärnu" → "parnu",
    // "Rīga" → "riga", "Jõhvi" → "johvi".
    private static string SlugifyCity(string city)
    {
        if (string.IsNullOrWhiteSpace(city)) return string.Empty;

        var normalized = city.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        var ascii = sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        // Collapse any run of whitespace into a single hyphen.
        return string.Join("-", ascii.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    // Tolerant parse of the blog.articles CMS value: returns (slug, lastMod) pairs,
    // skipping malformed entries. Slugs are constrained to the same charset the
    // frontend router accepts so a bad CMS edit can't inject markup into the XML.
    private static List<(string Slug, string? LastMod)> ParseBlogArticles(string? json)
    {
        var result = new List<(string, string?)>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return result;
            foreach (var article in doc.RootElement.EnumerateArray())
            {
                if (article.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                if (!article.TryGetProperty("slug", out var slugEl) ||
                    slugEl.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                var slug = slugEl.GetString();
                if (string.IsNullOrEmpty(slug) ||
                    !System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9-]{2,120}$")) continue;

                string? lastMod = null;
                if (article.TryGetProperty("publishedAt", out var pubEl) &&
                    pubEl.ValueKind == System.Text.Json.JsonValueKind.String &&
                    DateTime.TryParse(pubEl.GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var pub))
                    lastMod = pub.ToString("yyyy-MM-dd");

                result.Add((slug, lastMod));
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Malformed CMS value — sitemap simply omits blog posts rather than 500ing.
        }
        return result;
    }

    [HttpGet("sitemap.xml")]
    [HttpHead("sitemap.xml")]
    [Produces("application/xml")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Sitemap()
    {
        var listings = await db.Listings
            .Where(l => l.IsActive && l.Supplier != null && l.Supplier.IsActive)
            .Select(l => new { l.Id, l.Type, l.UpdatedAt })
            .ToListAsync();

        var locations = await db.SupplierLocations
            .Where(l => l.IsActive && l.Supplier != null && l.Supplier.IsActive)
            .Select(l => new { l.Id, l.UpdatedAt })
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"");
        sb.AppendLine("  xmlns:xhtml=\"http://www.w3.org/1999/xhtml\">");

        var staticPages = new[]
        {
            ("",              "1.0", "daily"),
            ("/request",      "0.9", "weekly"),
            ("/search",       "0.9", "daily"),
            ("/blog",         "0.7", "weekly"),
            ("/about",        "0.6", "monthly"),
            ("/contact",      "0.6", "monthly"),
            ("/how-it-works", "0.7", "monthly"),
            ("/faq",          "0.7", "monthly"),
            ("/provider",     "0.8", "weekly"),
            ("/terms",        "0.3", "yearly"),
            ("/privacy",      "0.3", "yearly"),
            ("/cookies",      "0.3", "yearly"),
        };

        foreach (var (path, priority, freq) in staticPages)
            AppendLangUrlSet(sb, path, priority, freq);

        var showMoving  = await db.PlatformSettings
            .Where(s => s.Key == "showMovingService")
            .Select(s => s.Value).FirstOrDefaultAsync() != "false";
        var showTrailer = await db.PlatformSettings
            .Where(s => s.Key == "showTrailerService")
            .Select(s => s.Value).FirstOrDefaultAsync() != "false";

        foreach (var listing in listings)
        {
            var type    = listing.Type.ToString().ToLower();
            if (type == "moving"  && !showMoving)  continue;
            if (type == "trailer" && !showTrailer) continue;
            var path    = $"/{type}/{listing.Id}";
            var lastMod = listing.UpdatedAt.ToString("yyyy-MM-dd");
            AppendLangUrlSet(sb, path, "0.8", "weekly", lastMod);
        }

        foreach (var location in locations)
        {
            var path    = $"/location/{location.Id}";
            var lastMod = location.UpdatedAt.ToString("yyyy-MM-dd");
            AppendLangUrlSet(sb, path, "0.9", "weekly", lastMod);
        }

        var partners = await db.Suppliers
            .Where(s => s.IsActive && s.Slug != null && s.IsPartnerPagePublished)
            .Select(s => new { s.Slug, s.UpdatedAt })
            .ToListAsync();

        foreach (var partner in partners)
        {
            var path    = $"/partner/{partner.Slug}";
            var lastMod = partner.UpdatedAt.ToString("yyyy-MM-dd");
            AppendLangUrlSet(sb, path, "0.8", "weekly", lastMod);
        }

        // Per-vertical city hubs: emit /storage/<city>, /moving/<city>, /trailer/<city>
        // only for cities that actually have an active listing of that vertical (gated by
        // the platform's showMoving/showTrailer flags). Slugs are ASCII, consistent with
        // the frontend CityPage routes ("Pärnu" → "parnu", "Rīga" → "riga").
        var rawCityTypes = await db.Listings
            .Where(l => l.IsActive && l.City != null && l.Supplier != null && l.Supplier.IsActive)
            .Select(l => new { l.City, l.Type })
            .Distinct()
            .ToListAsync();

        var cityHubs = new HashSet<string>();
        foreach (var ct in rawCityTypes)
        {
            var slug = SlugifyCity(ct.City!);
            if (slug.Length == 0) continue;
            var hub = ct.Type switch
            {
                Models.Enums.ListingType.Moving  => showMoving  ? "moving"  : null,
                Models.Enums.ListingType.Trailer => showTrailer ? "trailer" : null,
                _                                 => "storage",
            };
            if (hub is null) continue;
            cityHubs.Add($"/{hub}/{slug}");
        }

        foreach (var path in cityHubs)
            AppendLangUrlSet(sb, path, "0.8", "weekly");

        // Blog posts live in the PlatformSettings CMS (key "blog.articles", a JSON
        // array), not in a table — emit /blog/{slug} only while the blog is enabled.
        var blogEnabled = await db.PlatformSettings
            .Where(s => s.Key == "blog.enabled")
            .Select(s => s.Value).FirstOrDefaultAsync() == "true";
        if (blogEnabled)
        {
            var articlesJson = await db.PlatformSettings
                .Where(s => s.Key == "blog.articles")
                .Select(s => s.Value).FirstOrDefaultAsync();
            foreach (var (slug, lastMod) in ParseBlogArticles(articlesJson))
                AppendLangUrlSet(sb, $"/blog/{slug}", "0.6", "monthly", lastMod);
        }

        sb.AppendLine("</urlset>");

        return Content(sb.ToString(), "application/xml", new System.Text.UTF8Encoding(false));
    }

    [HttpGet("robots.txt")]
    [HttpHead("robots.txt")]
    [Produces("text/plain")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public IActionResult Robots()
    {
        var content =
            "User-agent: *\n" +
            "Allow: /\n" +
            // Unprefixed paths kept as defense-in-depth in case a bot hits the
            // URL before the language-redirect fires.
            "Disallow: /account\n" +
            "Disallow: /admin\n" +
            "Disallow: /provider/dashboard\n" +
            "Disallow: /provider/onboarding\n" +
            "Disallow: /book\n" +
            // Language-prefixed paths (the canonical URLs after the redirect).
            "Disallow: /*/account\n" +
            "Disallow: /*/admin\n" +
            "Disallow: /*/provider/dashboard\n" +
            "Disallow: /*/provider/onboarding\n" +
            "Disallow: /*/book\n" +
            "Disallow: /payment\n" +
            "Disallow: /*/payment\n" +
            "\n" +
            "# AI training crawlers — blocked\n" +
            "User-agent: Amazonbot\n" +
            "Disallow: /\n" +
            "\n" +
            "User-agent: Applebot-Extended\n" +
            "Disallow: /\n" +
            "\n" +
            "User-agent: Bytespider\n" +
            "Disallow: /\n" +
            "\n" +
            "User-agent: CCBot\n" +
            "Disallow: /\n" +
            "\n" +
            "User-agent: ClaudeBot\n" +
            "Disallow: /\n" +
            "\n" +
            "User-agent: Google-Extended\n" +
            "Disallow: /\n" +
            "\n" +
            "User-agent: GPTBot\n" +
            "Disallow: /\n" +
            "\n" +
            "User-agent: meta-externalagent\n" +
            "Disallow: /\n" +
            "\n" +
            $"Sitemap: {BaseUrl}/sitemap.xml\n";

        return Content(content, "text/plain");
    }
}
