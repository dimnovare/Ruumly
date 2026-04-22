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
}
