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

    [HttpGet("sitemap.xml")]
    [Produces("application/xml")]
    public async Task<IActionResult> Sitemap()
    {
        var listings = await db.Listings
            .Where(l => l.IsActive)
            .Select(l => new { l.Id, l.Type, l.UpdatedAt })
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

        var langs = new[] { "et", "en", "ru" };

        foreach (var (path, priority, freq) in staticPages)
        {
            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{BaseUrl}{path}</loc>");
            sb.AppendLine($"    <changefreq>{freq}</changefreq>");
            sb.AppendLine($"    <priority>{priority}</priority>");

            foreach (var lang in langs)
                sb.AppendLine($"    <xhtml:link rel=\"alternate\" hreflang=\"{lang}\" href=\"{BaseUrl}{path}?lang={lang}\"/>");

            sb.AppendLine($"    <xhtml:link rel=\"alternate\" hreflang=\"x-default\" href=\"{BaseUrl}{path}\"/>");
            sb.AppendLine("  </url>");
        }

        foreach (var listing in listings)
        {
            var type    = listing.Type.ToString().ToLower();
            var path    = $"/{type}/{listing.Id}";
            var lastMod = listing.UpdatedAt.ToString("yyyy-MM-dd");

            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{BaseUrl}{path}</loc>");
            sb.AppendLine($"    <lastmod>{lastMod}</lastmod>");
            sb.AppendLine("    <changefreq>weekly</changefreq>");
            sb.AppendLine("    <priority>0.8</priority>");

            foreach (var lang in langs)
                sb.AppendLine($"    <xhtml:link rel=\"alternate\" hreflang=\"{lang}\" href=\"{BaseUrl}{path}?lang={lang}\"/>");

            sb.AppendLine($"    <xhtml:link rel=\"alternate\" hreflang=\"x-default\" href=\"{BaseUrl}{path}\"/>");
            sb.AppendLine("  </url>");
        }

        sb.AppendLine("</urlset>");

        return Content(sb.ToString(), "application/xml", Encoding.UTF8);
    }

    [HttpGet("robots.txt")]
    [Produces("text/plain")]
    public IActionResult Robots()
    {
        var content =
            "User-agent: *\n" +
            "Allow: /\n" +
            "Disallow: /account\n" +
            "Disallow: /admin\n" +
            "Disallow: /provider/dashboard\n" +
            "Disallow: /provider/onboarding\n" +
            "Disallow: /book\n" +
            $"\nSitemap: {BaseUrl}/sitemap.xml\n";

        return Content(content, "text/plain");
    }
}
