using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ruumly.Backend.Helpers;

namespace Ruumly.Backend.Tests;

/// <summary>
/// The sanitized 400 factory backs POST /api/offers/{token}/choose (and every other
/// [ApiController]). Both a bad-Guid field and malformed JSON surface through the
/// SAME InvalidModelStateResponseFactory — this proves the resulting body carries no
/// internal .NET type names, no JSON byte offsets, and no traceId.
/// </summary>
public class ValidationProblem400Tests
{
    private static ActionContext MakeContext(params (string key, string message)[] errors)
    {
        var ctx = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        foreach (var (key, message) in errors)
            ctx.ModelState.AddModelError(key, message);
        return ctx;
    }

    private static (int status, string json) Render(ActionContext ctx)
    {
        var result = ValidationProblemFactory.Build(ctx)
            .Should().BeOfType<BadRequestObjectResult>().Subject;
        return (result.StatusCode ?? 0, JsonSerializer.Serialize(result.Value));
    }

    [Fact]
    public void Build_BadGuidAndMalformedJson_LeakNoTypeNamesBytePositionsOrTraceId()
    {
        // These are the verbatim System.Text.Json messages the framework default
        // factory surfaced on {"optionId":"not-a-guid"} and on malformed JSON.
        var ctx = MakeContext(
            ("$.optionId",
                "The JSON value could not be converted to System.Guid. " +
                "Path: $.optionId | LineNumber: 0 | BytePositionInLine: 25."),
            ("$",
                "The JSON value could not be converted to " +
                "Ruumly.Backend.DTOs.Requests.ChooseOptionRequest. " +
                "Path: $ | LineNumber: 0 | BytePositionInLine: 15."));

        var (status, json) = Render(ctx);

        status.Should().Be(400);
        json.Should().NotContain("Ruumly.Backend", "the internal DTO type name must never leak");
        json.Should().NotContain("System.Guid", "internal .NET type names must never leak");
        json.Should().NotContain("BytePositionInLine", "JSON byte offsets must never leak");
        json.Should().NotContain("LineNumber");
        json.Should().NotContain("could not be converted");
        json.Should().NotContain("traceId", "the sanitized body carries no request trace id");

        // Still a useful, stable envelope with a generic field-level message.
        json.Should().Contain("Bad Request").And.Contain("statusCode");
        json.Should().Contain("optionId", "the offending field name is preserved (path punctuation stripped)");
        json.Should().Contain("The value is invalid.");
    }

    [Fact]
    public void SanitizedFactory_WiredAfterAddControllers_WinsOverFrameworkDefault()
    {
        // Reproduces Program.cs's registration: AddControllers registers the
        // framework's leaky ApiBehaviorOptionsSetup, then our extension registers
        // the sanitized factory AFTER it — options config runs in registration
        // order, so ours must win. (The original bug configured it BEFORE
        // AddControllers, so the framework default silently overwrote it.)
        var provider = new ServiceCollection()
            .AddControllers()
            .AddSanitizedValidationErrors()
            .Services
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ApiBehaviorOptions>>().Value;

        options.InvalidModelStateResponseFactory.Method.Should().BeSameAs(
            typeof(ValidationProblemFactory).GetMethod(nameof(ValidationProblemFactory.Build)),
            "the sanitized factory must be the effective InvalidModelStateResponseFactory");
    }

    [Fact]
    public void Build_PreservesCleanValidationMessages()
    {
        // Human-authored FluentValidation / DataAnnotations messages are safe and
        // must survive sanitization intact.
        var (_, json) = Render(MakeContext(("Email", "Email is required.")));

        json.Should().Contain("Email is required.");
        json.Should().NotContain("The value is invalid.");
    }
}
