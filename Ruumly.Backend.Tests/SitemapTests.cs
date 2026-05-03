using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Controllers;

namespace Ruumly.Backend.Tests;

public class SitemapTests
{
    private const string BaseUrl = "https://ruumly.eu";
    private static readonly string[] Langs = ["et", "en", "ru", "lv", "lt"];

    private static async Task<string> RenderSitemapAsync()
    {
        var db = TestDbContext.Create();
        var controller = new SitemapController(db);
        var result = await controller.Sitemap() as ContentResult;
        return result!.Content!;
    }

    private static IReadOnlyList<string> SplitUrlBlocks(string body) =>
        Regex.Matches(body, @"<url>(?:.|\n)*?</url>")
             .Select(m => m.Value)
             .ToList();

    [Fact]
    public async Task SitemapController_NoLangQueryParams()
    {
        var body = await RenderSitemapAsync();

        body.Should().NotContain("?lang=",
            "hreflang alternates must use language-prefixed paths, not ?lang= variants");
        body.Should().Contain("hreflang=\"et\"");
        body.Should().Contain("hreflang=\"en\"");
        body.Should().Contain("hreflang=\"x-default\"");
    }

    [Fact]
    public async Task Sitemap_HomepageEmits_FiveLanguagePrefixedLocs_NoTrailingSlash()
    {
        var body = await RenderSitemapAsync();

        // Each lang gets its own loc — no trailing slash on the homepage.
        foreach (var lang in Langs)
        {
            body.Should().Contain($"<loc>{BaseUrl}/{lang}</loc>",
                $"homepage must produce a loc entry for /{lang}");
        }

        body.Should().NotContain($"<loc>{BaseUrl}/et/</loc>",
            "homepage loc must not have a trailing slash");
        body.Should().NotContain($"<loc>{BaseUrl}</loc>",
            "the legacy unprefixed homepage loc must not appear");
    }

    [Fact]
    public async Task Sitemap_StaticPage_EmitsFiveLocsAndSixHreflangsPerBlock()
    {
        var body = await RenderSitemapAsync();

        // Five lang-prefixed locs for a known static page.
        foreach (var lang in Langs)
            body.Should().Contain($"<loc>{BaseUrl}/{lang}/about</loc>");

        // Each of those five blocks must carry exactly 6 xhtml:link entries
        // (5 per-language + 1 x-default).
        var aboutBlocks = SplitUrlBlocks(body)
            .Where(b => b.Contains("/about</loc>"))
            .ToList();
        aboutBlocks.Should().HaveCount(5, "one block per language");

        foreach (var block in aboutBlocks)
        {
            var linkCount = Regex.Matches(block, @"<xhtml:link\b").Count;
            linkCount.Should().Be(6,
                "every <url> block must carry 5 hreflang alternates plus x-default");
        }
    }

    [Fact]
    public async Task Sitemap_XDefault_AlwaysPointsToEtVersion()
    {
        var body = await RenderSitemapAsync();

        var defaults = Regex.Matches(body,
            @"hreflang=""x-default"" href=""([^""]+)""");
        defaults.Count.Should().BeGreaterThan(0, "x-default alternates must be emitted");

        foreach (Match match in defaults)
        {
            var href = match.Groups[1].Value;
            // Either {BaseUrl}/et exactly (homepage) or {BaseUrl}/et/<path>.
            var isHomepage = href == $"{BaseUrl}/et";
            var isEtPath   = href.StartsWith($"{BaseUrl}/et/", StringComparison.Ordinal);
            (isHomepage || isEtPath).Should().BeTrue(
                $"x-default href must point to the et version, was: {href}");
        }
    }

    [Fact]
    public async Task Sitemap_CityPages_UseLanguagePrefix()
    {
        var body = await RenderSitemapAsync();

        // City pages previously emitted no hreflang at all — verify they now
        // follow the same per-language pattern as everything else.
        foreach (var lang in Langs)
        {
            body.Should().Contain($"<loc>{BaseUrl}/{lang}/storage/tallinn</loc>");
            body.Should().Contain(
                $"<xhtml:link rel=\"alternate\" hreflang=\"{lang}\" href=\"{BaseUrl}/{lang}/storage/tallinn\"/>");
        }

        // x-default for the city block points to /et/.
        body.Should().Contain(
            $"<xhtml:link rel=\"alternate\" hreflang=\"x-default\" href=\"{BaseUrl}/et/storage/tallinn\"/>");
    }

    // Google's sitemap fetcher does a HEAD probe before the GET. Without
    // [HttpHead] alongside [HttpGet], ASP.NET returns 405 and Google reports
    // "Couldn't fetch" in Search Console. RFC 7231 requires HEAD support on
    // any GET-supporting resource.
    [Fact]
    public void Sitemap_AllowsHeadMethod()
    {
        var method = typeof(SitemapController).GetMethod(nameof(SitemapController.Sitemap));
        method.Should().NotBeNull();

        var headAttrs = method!.GetCustomAttributes<HttpHeadAttribute>(inherit: true).ToList();
        headAttrs.Should().ContainSingle("Sitemap must accept HEAD requests");
        headAttrs[0].Template.Should().Be("sitemap.xml",
            "HEAD route must match the GET route exactly");

        var getAttrs = method!.GetCustomAttributes<HttpGetAttribute>(inherit: true).ToList();
        getAttrs.Should().ContainSingle();
        getAttrs[0].Template.Should().Be("sitemap.xml");
    }
}
