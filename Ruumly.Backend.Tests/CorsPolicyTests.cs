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
    private static CorsPolicy BuildPolicy(params string[] allowedOrigins)
    {
        var services = new ServiceCollection();
        services.AddCors(options => CorsPolicySetup.AddFrontendPolicy(options, allowedOrigins));
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
        BuildPolicy("https://ruumly.eu").ExposedHeaders
            .Should().Contain("Retry-After");
    }

    [Fact]
    public void FrontendPolicy_StillRestrictsOrigins_AndKeepsCredentials()
    {
        var policy = BuildPolicy("https://ruumly.eu");

        policy.SupportsCredentials.Should().BeTrue();
        policy.AllowAnyHeader.Should().BeTrue();
        policy.AllowAnyMethod.Should().BeTrue();
        policy.AllowAnyOrigin.Should().BeFalse("origins stay gated by the allow-list");

        policy.IsOriginAllowed!("https://ruumly.eu").Should().BeTrue();
        policy.IsOriginAllowed!("https://estonia-space-hub.vercel.app").Should().BeTrue();
        policy.IsOriginAllowed!("https://estonia-space-hub-a1b2c3-ruumly.vercel.app").Should().BeTrue(
            "Vercel preview deploys of the real project are allowed");

        policy.IsOriginAllowed!("https://ruumly-evil.vercel.app").Should().BeFalse(
            "a short prefix would let an attacker register a lookalike Vercel slug " +
            "and make credentialed cross-origin requests");
        policy.IsOriginAllowed!("https://evil.com").Should().BeFalse();
        policy.IsOriginAllowed!("https://estonia-space-hub.vercel.app.evil.com").Should().BeFalse();
        policy.IsOriginAllowed!("not-a-url").Should().BeFalse();
    }
}
