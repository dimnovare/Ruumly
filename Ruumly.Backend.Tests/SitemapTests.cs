using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Controllers;

namespace Ruumly.Backend.Tests;

public class SitemapTests
{
    [Fact]
    public async Task SitemapController_NoLangQueryParams()
    {
        var db = TestDbContext.Create();
        var controller = new SitemapController(db);

        var result = await controller.Sitemap();

        var content = result as ContentResult;
        content.Should().NotBeNull();
        content!.StatusCode.Should().BeNull("ContentResult defaults to 200");
        content.ContentType.Should().Contain("application/xml");

        var body = content.Content!;
        body.Should().NotContain("?lang=",
            "hreflang alternates must use the canonical URL, not ?lang= variants");
        body.Should().Contain("hreflang=\"et\"");
        body.Should().Contain("hreflang=\"en\"");
        body.Should().Contain("hreflang=\"x-default\"");
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

        var getAttrs = method.GetCustomAttributes<HttpGetAttribute>(inherit: true).ToList();
        getAttrs.Should().ContainSingle();
        getAttrs[0].Template.Should().Be("sitemap.xml");
    }
}
