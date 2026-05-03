using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Controllers;

namespace Ruumly.Backend.Tests;

public class RobotsTxtTests
{
    [Fact]
    public void RobotsController_SingleUserAgentStar()
    {
        var db = TestDbContext.Create();
        var controller = new SitemapController(db);

        var result = controller.Robots() as ContentResult;

        result.Should().NotBeNull();
        result!.ContentType.Should().Contain("text/plain");

        var body = result.Content!;

        // Exactly one User-agent: * block
        var starCount = Regex.Matches(body, @"User-agent: \*").Count;
        starCount.Should().Be(1, "there must be exactly one User-agent: * block");

        // Sitemap directive present
        body.Should().Contain("Sitemap: https://ruumly.eu/sitemap.xml");

        // No Cloudflare managed content markers
        body.Should().NotContain("Cloudflare Managed content");
        body.Should().NotContain("Content-Signal:");
    }

    [Fact]
    public void RobotsController_LanguagePrefixedDisallowsPresent()
    {
        var db = TestDbContext.Create();
        var controller = new SitemapController(db);
        var body = ((ContentResult)controller.Robots()).Content!;

        // Wildcard prefixes for the language-prefixed canonical URLs.
        body.Should().Contain("Disallow: /*/account");
        body.Should().Contain("Disallow: /*/admin");
        body.Should().Contain("Disallow: /*/provider/dashboard");
        body.Should().Contain("Disallow: /*/provider/onboarding");
        body.Should().Contain("Disallow: /*/book");

        // Defense-in-depth: original unprefixed paths still present.
        body.Should().Contain("Disallow: /account");
        body.Should().Contain("Disallow: /admin");
        body.Should().Contain("Disallow: /provider/dashboard");
        body.Should().Contain("Disallow: /provider/onboarding");
        body.Should().Contain("Disallow: /book");
    }

    // Google does a HEAD probe before fetching robots.txt. Without [HttpHead]
    // alongside [HttpGet], ASP.NET returns 405 and the crawler skips the file.
    [Fact]
    public void Robots_AllowsHeadMethod()
    {
        var method = typeof(SitemapController).GetMethod(nameof(SitemapController.Robots));
        method.Should().NotBeNull();

        var headAttrs = method!.GetCustomAttributes<HttpHeadAttribute>(inherit: true).ToList();
        headAttrs.Should().ContainSingle("Robots must accept HEAD requests");
        headAttrs[0].Template.Should().Be("robots.txt",
            "HEAD route must match the GET route exactly");

        var getAttrs = method!.GetCustomAttributes<HttpGetAttribute>(inherit: true).ToList();
        getAttrs.Should().ContainSingle();
        getAttrs[0].Template.Should().Be("robots.txt");
    }
}
