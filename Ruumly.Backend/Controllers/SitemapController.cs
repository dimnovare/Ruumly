using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using System.Text;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("")]
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

    [HttpGet("sitemap.xml")]
    [HttpHead("sitemap.xml")]
    [Produces("application/xml")]
    public async Task<IActionResult> Sitemap()
    {
        var listings = await db.Listings
            .Where(l => l.IsActive)
            .Select(l => new { l.Id, l.Type, l.UpdatedAt })
            .ToListAsync();

        var locations = await db.SupplierLocations
            .Where(l => l.IsActive)
            .Select(l => new { l.Id, l.UpdatedAt })
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"");
        sb.AppendLine("  xmlns:xhtml=\"http://www.w3.org/1999/xhtml\">");

        var staticPages = new[]
        {
            ("",              "1.0", "daily"),
            ("/search",       "0.9", "daily"),
            ("/about",        "0.6", "monthly"),
            ("/contact",      "0.6", "monthly"),
            ("/how-it-works", "0.7", "monthly"),
            ("/faq",          "0.7", "monthly"),
            ("/provider",     "0.8", "weekly"),
            ("/terms",        "0.3", "yearly"),
            ("/privacy",      "0.3", "yearly"),
        };

        foreach (var (path, priority, freq) in staticPages)
            AppendLangUrlSet(sb, path, priority, freq);

        foreach (var listing in listings)
        {
            var type    = listing.Type.ToString().ToLower();
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
            .Where(s => s.IsActive && s.Slug != null)
            .Select(s => new { s.Slug, s.UpdatedAt })
            .ToListAsync();

        foreach (var partner in partners)
        {
            var path    = $"/partner/{partner.Slug}";
            var lastMod = partner.UpdatedAt.ToString("yyyy-MM-dd");
            AppendLangUrlSet(sb, path, "0.8", "weekly", lastMod);
        }

        var cityPages = new[] { "tallinn", "riga", "vilnius" };
        foreach (var city in cityPages)
            AppendLangUrlSet(sb, $"/storage/{city}", "0.8", "weekly");

        sb.AppendLine("</urlset>");

        return Content(sb.ToString(), "application/xml", Encoding.UTF8);
    }

    [HttpGet("robots.txt")]
    [HttpHead("robots.txt")]
    [Produces("text/plain")]
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
