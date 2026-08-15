using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

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

    // Renders the sitemap with one active Tallinn listing seeded, so the
    // city-page branch of the controller actually emits a /storage/tallinn block.
    private static async Task<string> RenderSitemapWithTallinnListingAsync()
    {
        var db = TestDbContext.Create();

        var supplier = new Supplier
        {
            Id           = Guid.NewGuid(),
            Name         = "Test Supplier",
            ContactName  = "Contact",
            ContactEmail = "s@test.ee",
            ContactPhone = "+372 5000 0000",
            IsActive     = true,
        };
        var listing = new Listing
        {
            Id          = Guid.NewGuid(),
            SupplierId  = supplier.Id,
            Type        = ListingType.Warehouse,
            Title       = "Tallinn Storage",
            Address     = "Test St 1",
            City        = "Tallinn",
            PriceUnit   = "kuu",
            Description = "Test",
            IsActive    = true,
        };
        db.Suppliers.Add(supplier);
        db.Listings.Add(listing);
        await db.SaveChangesAsync();

        var controller = new SitemapController(db);
        var result = await controller.Sitemap() as ContentResult;
        return result!.Content!;
    }

    // A directory provider (unclaimed free profile) holds its supply as a zero-unit
    // SupplierLocation, NOT a Listing. In prod that IS the entire supply, so the
    // sitemap must derive city hubs from directory locations too — otherwise the
    // ranking /storage/{city} pages never enter the sitemap. Seeds a single directory
    // location in `city` (with NO Listings anywhere) so only the directory branch can
    // produce the hub.
    private static async Task<string> RenderSitemapWithDirectoryLocationAsync(
        string city, string? serviceTypesJson)
    {
        var db = TestDbContext.Create();

        var supplier = new Supplier
        {
            Id                 = Guid.NewGuid(),
            Name               = "Directory Provider",
            ContactName        = "Contact",
            ContactEmail       = "d@test.ee",
            ContactPhone       = "+372 5000 0001",
            IsActive           = true,
            IsDirectoryListing = true,
            ServiceTypesJson   = serviceTypesJson,
        };
        var location = new SupplierLocation
        {
            Id         = Guid.NewGuid(),
            SupplierId = supplier.Id,
            Name       = $"{city} depot",
            Address    = "Test St 2",
            City       = city,
            IsActive   = true,
        };
        db.Suppliers.Add(supplier);
        db.SupplierLocations.Add(location);
        await db.SaveChangesAsync();

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
        var body = await RenderSitemapWithTallinnListingAsync();

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

    [Fact]
    public async Task Sitemap_DirectoryOnlyCity_EmitsStorageHubWithHreflang()
    {
        // No Listings seeded at all — the city hub must come from the directory
        // location. Before the fix this city produced ZERO url blocks (prod symptom).
        var body = await RenderSitemapWithDirectoryLocationAsync("Tartu", serviceTypesJson: null);

        foreach (var lang in Langs)
        {
            body.Should().Contain($"<loc>{BaseUrl}/{lang}/storage/tartu</loc>",
                "directory-only cities must still produce a /storage/{city} hub");
            body.Should().Contain(
                $"<xhtml:link rel=\"alternate\" hreflang=\"{lang}\" href=\"{BaseUrl}/{lang}/storage/tartu\"/>");
        }

        // x-default for the directory city block points to /et/.
        body.Should().Contain(
            $"<xhtml:link rel=\"alternate\" hreflang=\"x-default\" href=\"{BaseUrl}/et/storage/tartu\"/>");
    }

    [Fact]
    public async Task Sitemap_DirectoryCity_EmitsServiceHubsFromServiceTypesOnly()
    {
        // A directory provider advertising storage + moving yields both hubs and
        // never invents a /trailer hub it doesn't serve.
        var body = await RenderSitemapWithDirectoryLocationAsync(
            "Narva", serviceTypesJson: """["warehouse","moving"]""");

        body.Should().Contain($"<loc>{BaseUrl}/et/storage/narva</loc>");
        body.Should().Contain($"<loc>{BaseUrl}/et/moving/narva</loc>");
        body.Should().NotContain($"<loc>{BaseUrl}/et/trailer/narva</loc>",
            "no directory provider in this city advertises the trailer vertical");
    }

    [Fact]
    public async Task Sitemap_DirectoryCity_EmitsDirectoryOnlyCategoryHubs()
    {
        // A directory provider advertising the directory-only consumer categories
        // (cleaning + vanrental) must surface /cleaning/{city} and /vanrental/{city}
        // hubs — and never invent hubs for categories it doesn't advertise.
        var body = await RenderSitemapWithDirectoryLocationAsync(
            "Tallinn", serviceTypesJson: """["cleaning","vanrental"]""");

        foreach (var lang in Langs)
        {
            body.Should().Contain($"<loc>{BaseUrl}/{lang}/cleaning/tallinn</loc>",
                "a directory provider advertising cleaning yields a /cleaning/{city} hub");
            body.Should().Contain(
                $"<xhtml:link rel=\"alternate\" hreflang=\"{lang}\" href=\"{BaseUrl}/{lang}/cleaning/tallinn\"/>");
        }
        body.Should().Contain($"<loc>{BaseUrl}/et/vanrental/tallinn</loc>");
        body.Should().NotContain($"<loc>{BaseUrl}/et/moving/tallinn</loc>",
            "no provider in this city advertises moving");
        body.Should().NotContain($"<loc>{BaseUrl}/et/storage/tallinn</loc>",
            "an explicit service list without warehouse suppresses the storage anchor");
    }

    // 2026-08: we stopped selling packing (an add-on of a moving request) and
    // insurance (B2B carrier liability here, not a household product). A supplier
    // may still CARRY those service types — that is useful supply-side metadata —
    // but we must not ask Google to rank a city page we cannot serve.
    [Fact]
    public async Task Sitemap_NeverEmitsPackingOrInsuranceHubs_EvenWhenAProviderAdvertisesThem()
    {
        var body = await RenderSitemapWithDirectoryLocationAsync(
            "Tallinn", serviceTypesJson: """["warehouse","moving","cleaning","vanrental","packing","insurance"]""");

        // The retired pair is gone from every language.
        foreach (var lang in Langs)
        {
            body.Should().NotContain($"/{lang}/packing/tallinn",
                "packing is an add-on of a moving request, not a bookable service");
            body.Should().NotContain($"/{lang}/insurance/tallinn",
                "consumer insurance is not a product we can fulfil");
        }

        // Everything else this provider advertises still ships.
        foreach (var hub in new[] { "storage", "moving", "cleaning", "vanrental" })
        {
            body.Should().Contain($"<loc>{BaseUrl}/et/{hub}/tallinn</loc>",
                $"/{hub}/{{city}} is still a live vertical");
            body.Should().Contain($"<loc>{BaseUrl}/ru/{hub}/tallinn</loc>");
        }
    }

    private static async Task<string> RenderSitemapWithBlogAsync(string? enabled, string? articlesJson)
    {
        var db = TestDbContext.Create();
        if (enabled is not null)
            db.PlatformSettings.Add(new PlatformSetting { Key = "blog.enabled", Value = enabled });
        if (articlesJson is not null)
            db.PlatformSettings.Add(new PlatformSetting { Key = "blog.articles", Value = articlesJson });
        await db.SaveChangesAsync();

        var controller = new SitemapController(db);
        var result = await controller.Sitemap() as ContentResult;
        return result!.Content!;
    }

    [Fact]
    public async Task Sitemap_BlogPosts_EmittedPerLanguage_WhenBlogEnabled()
    {
        const string articles =
            """[{"slug":"kolimise-checklist","publishedAt":"2026-07-09T10:00:00Z"},{"slug":"mis-on-ruumly"}]""";
        var body = await RenderSitemapWithBlogAsync("true", articles);

        foreach (var lang in Langs)
            body.Should().Contain($"<loc>{BaseUrl}/{lang}/blog/kolimise-checklist</loc>");
        body.Should().Contain($"<loc>{BaseUrl}/et/blog/mis-on-ruumly</loc>");

        // publishedAt flows through as lastmod on the dated article.
        var datedBlock = SplitUrlBlocks(body)
            .First(b => b.Contains($"<loc>{BaseUrl}/et/blog/kolimise-checklist</loc>"));
        datedBlock.Should().Contain("<lastmod>2026-07-09</lastmod>");
    }

    [Fact]
    public async Task Sitemap_BlogPosts_Omitted_WhenBlogDisabledOrUnset()
    {
        const string articles = """[{"slug":"kolimise-checklist"}]""";

        var disabled = await RenderSitemapWithBlogAsync("false", articles);
        disabled.Should().NotContain("/blog/kolimise-checklist");

        var unset = await RenderSitemapWithBlogAsync(null, articles);
        unset.Should().NotContain("/blog/kolimise-checklist");
    }

    [Fact]
    public async Task Sitemap_BlogPosts_MalformedJsonAndBadSlugs_AreSkippedSafely()
    {
        // Malformed JSON must not 500 the sitemap.
        var malformed = await RenderSitemapWithBlogAsync("true", "{not json");
        malformed.Should().Contain("<urlset");
        malformed.Should().NotContain("/blog/{");

        // Slugs outside ^[a-z0-9-]{2,120}$ (markup, uppercase) are dropped.
        const string articles =
            """[{"slug":"<script>alert(1)</script>"},{"slug":"Valid-Not"},{"slug":"ok-post"}]""";
        var body = await RenderSitemapWithBlogAsync("true", articles);
        body.Should().NotContain("<script>");
        body.Should().NotContain("/blog/Valid-Not");
        body.Should().Contain($"<loc>{BaseUrl}/et/blog/ok-post</loc>");
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

    /// <summary>
    /// The legal pages render `robots: noindex,nofollow` — the React SEO
    /// component always set it, and since 2026-08-14 the prerendered HTML
    /// carries it too. Listing a noindex URL in the sitemap is a contradiction:
    /// the sitemap asks Google to index a page that forbids indexing, and it
    /// spends crawl budget on URLs that can never rank.
    /// </summary>
    [Fact]
    public async Task Sitemap_OmitsNoindexLegalPages()
    {
        var db = TestDbContext.Create();
        var controller = new SitemapController(db);

        var result = await controller.Sitemap() as ContentResult;
        var xml = result!.Content!;

        foreach (var path in new[] { "/terms", "/privacy", "/cookies" })
            xml.Should().NotContain(path, $"{path} is noindex and must not be advertised for indexing");
    }

    /// <summary>
    /// The commercial front door must not rank below the pages it feeds.
    /// </summary>
    [Fact]
    public async Task Sitemap_RanksTheRequestFunnelAboveTheCityHubs()
    {
        var db = TestDbContext.Create();
        var controller = new SitemapController(db);

        var result = await controller.Sitemap() as ContentResult;
        var xml = result!.Content!;

        xml.Should().Contain("/et/request");
        // 0.9 for the funnel, 0.8 for the service x city hubs.
        xml.Should().Contain("<priority>0.9</priority>");
    }
}
