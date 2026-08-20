using FluentAssertions;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ruumly.Backend.Helpers;

namespace Ruumly.Backend.Tests;

/// <summary>
/// The SPA CORS policy — built through the real AddCors pipeline using the same
/// setup call Program.cs makes.
/// </summary>
public class CorsPolicyTests
{
    private static CorsPolicy BuildPolicy(string[] allowedOrigins, string? vercelTeamSlug = null)
    {
        var services = new ServiceCollection();
        services.AddCors(options =>
            CorsPolicySetup.AddFrontendPolicy(options, allowedOrigins, vercelTeamSlug));
        return services.BuildServiceProvider()
            .GetRequiredService<IOptions<CorsOptions>>().Value
            .GetPolicy(CorsPolicySetup.PolicyName)!;
    }

    [Fact]
    public void FrontendPolicy_ExposesRetryAfter_SoTheSpaCanReadTheRateLimitBackoff()
    {
        // Retry-After is NOT CORS-safelisted, and api.ruumly.eu is a different
        // origin from ruumly.eu — without this the browser hides the header from
        // fetch() and the 429 countdown silently degrades to "try again later".
        BuildPolicy(["https://ruumly.eu"]).ExposedHeaders
            .Should().Contain("Retry-After");
    }

    [Fact]
    public void FrontendPolicy_StillRestrictsOrigins_AndKeepsCredentials()
    {
        var policy = BuildPolicy(["https://ruumly.eu"]);

        policy.SupportsCredentials.Should().BeTrue();
        policy.AllowAnyHeader.Should().BeTrue();
        policy.AllowAnyMethod.Should().BeTrue();
        policy.AllowAnyOrigin.Should().BeFalse("origins stay gated by the allow-list");

        policy.IsOriginAllowed!("https://ruumly.eu").Should().BeTrue();
        // The project's own production alias is a fixed, globally-unique host.
        policy.IsOriginAllowed!("https://estonia-space-hub.vercel.app").Should().BeTrue();

        policy.IsOriginAllowed!("https://evil.com").Should().BeFalse();
        policy.IsOriginAllowed!("https://estonia-space-hub.vercel.app.evil.com").Should().BeFalse();
        policy.IsOriginAllowed!("not-a-url").Should().BeFalse();
    }

    // ─── The prefix hole, both instances ──────────────────────────────────────
    // The prior rule matched any host STARTING WITH `estonia-space-hub-`, so the
    // CVE-class lookalike an attacker can actually register slipped through.

    [Fact]
    public void FrontendPolicy_WithNoTeamSlug_TrustsNoPreviewHost_FailClosed()
    {
        var policy = BuildPolicy(["https://ruumly.eu"]);

        // The exact host the old regression test missed — anyone can register a
        // Vercel project named `estonia-space-hub-evil`.
        policy.IsOriginAllowed!("https://estonia-space-hub-evil.vercel.app").Should().BeFalse(
            "the project-name PREFIX is forgeable; without a pinned team slug no " +
            "preview host may hold a credentialed cross-origin session");
        policy.IsOriginAllowed!("https://estonia-space-hub-a1b2c3-ruumly.vercel.app").Should().BeFalse(
            "fail closed: even a well-formed preview is untrusted until the slug is set");
        policy.IsOriginAllowed!("https://ruumly-evil.vercel.app").Should().BeFalse();
    }

    [Fact]
    public void FrontendPolicy_WithTeamSlug_TrustsOnlyThatTeamsPreviews()
    {
        const string slug = "dimnovare-9994s-projects";
        var policy = BuildPolicy(["https://ruumly.eu"], vercelTeamSlug: slug);

        // The REAL shape, taken from `vercel ls` on 2026-08-20. Vercel truncates
        // the project name — asserting the literal observed host is what stops
        // this rule from silently matching none of our own deployments again.
        policy.IsOriginAllowed!($"https://estonia-space-e0ijg6mxw-{slug}.vercel.app").Should().BeTrue(
            "this is verbatim a real Ruumly deployment host");
        policy.IsOriginAllowed!($"https://estonia-space-hub-git-main-{slug}.vercel.app").Should().BeTrue(
            "branch-preview hosts carry the same trailing team slug");

        // The team slug is the one segment Vercel will not issue to an attacker.
        policy.IsOriginAllowed!("https://estonia-space-hub-evil.vercel.app").Should().BeFalse(
            "no trailing team slug ⇒ not our team's deployment");
        policy.IsOriginAllowed!("https://estonia-space-e0ijg6mxw-attacker.vercel.app").Should().BeFalse();
        // A slug SUFFIX must be preceded by a hyphen — no substring shortcut.
        policy.IsOriginAllowed!($"https://estonia-space-not{slug}.vercel.app").Should().BeFalse();
    }
}
