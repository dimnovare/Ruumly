using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ruumly.Backend.Data;
using Ruumly.Backend.Filters;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Tests;

public class EmailVerificationEnforcementTests
{
    // ─── Test infrastructure ───────────────────────────────────────────────

    private static RuumlyDbContext CreateDb() => TestDbContext.Create();

    private static (ActionExecutingContext executingCtx, ActionContext actionContext) MakeContext(RuumlyDbContext db, Guid userId, bool isAuthenticated = true)
    {
        var identity = isAuthenticated
            ? new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "test")
            : new ClaimsIdentity(); // anonymous

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
            RequestServices = new ServiceCollection()
                .AddSingleton(db)
                .BuildServiceProvider()
        };

        var actionContext  = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var executingCtx   = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());
        return (executingCtx, actionContext);
    }

    private static ActionExecutionDelegate MakeNextDelegate(ActionContext actionContext, out Func<bool> wasCalled)
    {
        var called = false;
        wasCalled = () => called;
        return () =>
        {
            called = true;
            var executedCtx = new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object());
            return Task.FromResult(executedCtx);
        };
    }

    // ─── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnverifiedUser_Gets_403_With_EmailNotVerified_Error()
    {
        // Arrange
        var db = CreateDb();
        var userId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id            = userId,
            Name          = "Unverified User",
            Email         = "unverified@ruumly.ee",
            PasswordHash  = "hash",
            Role          = UserRole.Customer,
            Status        = UserStatus.Active,
            Language      = "en",
            EmailVerified = false,
        });
        await db.SaveChangesAsync();

        var filter = new RequireEmailVerifiedFilter(db);
        var (ctx, actionContext) = MakeContext(db, userId);
        var next   = MakeNextDelegate(actionContext, out var wasCalled);

        // Act
        await filter.OnActionExecutionAsync(ctx, next);

        // Assert
        wasCalled().Should().BeFalse();
        ctx.Result.Should().BeOfType<ObjectResult>();
        var result = (ObjectResult)ctx.Result!;
        result.StatusCode.Should().Be(403);

        // The anonymous object has an `error` property equal to "EMAIL_NOT_VERIFIED"
        var value = result.Value!;
        var errorProp = value.GetType().GetProperty("error")?.GetValue(value) as string;
        errorProp.Should().Be("EMAIL_NOT_VERIFIED");
    }

    [Fact]
    public async Task VerifiedUser_Passes_Through_Filter()
    {
        // Arrange
        var db = CreateDb();
        var userId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id            = userId,
            Name          = "Verified User",
            Email         = "verified@ruumly.ee",
            PasswordHash  = "hash",
            Role          = UserRole.Customer,
            Status        = UserStatus.Active,
            Language      = "en",
            EmailVerified = true,
        });
        await db.SaveChangesAsync();

        var filter = new RequireEmailVerifiedFilter(db);
        var (ctx, actionContext) = MakeContext(db, userId);
        var next   = MakeNextDelegate(actionContext, out var wasCalled);

        // Act
        await filter.OnActionExecutionAsync(ctx, next);

        // Assert
        wasCalled().Should().BeTrue();
        ctx.Result.Should().BeNull();
    }

    [Fact]
    public async Task GoogleUser_WithEmailVerified_Passes_Through_Filter()
    {
        // Arrange
        var db = CreateDb();
        var userId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id            = userId,
            Name          = "Google User",
            Email         = "google@ruumly.ee",
            PasswordHash  = string.Empty,
            Role          = UserRole.Customer,
            Status        = UserStatus.Active,
            Language      = "en",
            EmailVerified = true,
            GoogleId      = "google-123",
        });
        await db.SaveChangesAsync();

        var filter = new RequireEmailVerifiedFilter(db);
        var (ctx, actionContext) = MakeContext(db, userId);
        var next   = MakeNextDelegate(actionContext, out var wasCalled);

        // Act
        await filter.OnActionExecutionAsync(ctx, next);

        // Assert
        wasCalled().Should().BeTrue();
        ctx.Result.Should().BeNull();
    }
}
