using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;
using BC = BCrypt.Net.BCrypt;

namespace Ruumly.Backend.Tests;

public class AuthServiceTests
{
    // ─── Test infrastructure ───────────────────────────────────────────────

    private static RuumlyDbContext CreateDb() => TestDbContext.Create();

    private static IConfiguration MakeConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"]                   = "test-secret-key-minimum-sixteen-chars",
                ["Jwt:Issuer"]                   = "ruumly-api",
                ["Jwt:Audience"]                 = "ruumly-frontend",
                ["Jwt:AccessTokenExpiryMinutes"] = "15",
                ["Jwt:RefreshTokenExpiryDays"]   = "7",
                ["AppUrl"]                       = "https://test.ruumly.eu",
            })
            .Build();

    private static AuthService MakeService(RuumlyDbContext db) =>
        new(db, MakeConfig(), new NoOpEmailSender(), new NoOpHttpContextAccessor());

    private sealed class NoOpEmailSender : IEmailSender
    {
        public Task SendAsync(string to, string subject, string textBody, string? htmlBody = null)
            => Task.CompletedTask;
    }

    private sealed class NoOpHttpContextAccessor : Microsoft.AspNetCore.Http.IHttpContextAccessor
    {
        public Microsoft.AspNetCore.Http.HttpContext? HttpContext { get; set; }
    }

    private static RegisterRequest MakeRegisterRequest(
        string email    = "test@ruumly.ee",
        string name     = "Test User",
        string password = "Password123") =>
        new(name, email, password, password, null, "et");

    // ─── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_Creates_User_With_Hashed_Password()
    {
        var db      = CreateDb();
        var service = MakeService(db);

        var response = await service.RegisterAsync(MakeRegisterRequest());

        var user = await db.Users.FirstAsync();
        user.Email.Should().Be("test@ruumly.ee");
        user.PasswordHash.Should().NotBe("Password123");
        user.PasswordHash.Should().StartWith("$2"); // BCrypt prefix
        BC.Verify("Password123", user.PasswordHash).Should().BeTrue();

        response.User.Should().NotBeNull();
        response.AccessToken.Should().NotBeNullOrWhiteSpace();
        response.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Register_Rejects_Duplicate_Email()
    {
        var db      = CreateDb();
        var service = MakeService(db);

        await service.RegisterAsync(MakeRegisterRequest());

        var act = async () => await service.RegisterAsync(MakeRegisterRequest());

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Login_Returns_Tokens_For_Valid_Credentials()
    {
        var db      = CreateDb();
        var service = MakeService(db);

        await service.RegisterAsync(MakeRegisterRequest());

        var response = await service.LoginAsync(new LoginRequest("test@ruumly.ee", "Password123"));

        response.AccessToken.Should().NotBeNullOrWhiteSpace();
        response.RefreshToken.Should().NotBeNullOrWhiteSpace();
        response.User.Email.Should().Be("test@ruumly.ee");
    }

    [Fact]
    public async Task Login_Rejects_Wrong_Password()
    {
        var db = CreateDb();
        // Create user directly with known hash — faster than going through RegisterAsync again
        db.Users.Add(new User
        {
            Id           = Guid.NewGuid(),
            Email        = "test@ruumly.ee",
            Name         = "Test",
            PasswordHash = BC.HashPassword("CorrectPassword", workFactor: 4),
            Role         = UserRole.Customer,
            Status       = UserStatus.Active,
        });
        await db.SaveChangesAsync();

        var service = MakeService(db);

        var act = async () =>
            await service.LoginAsync(new LoginRequest("test@ruumly.ee", "WrongPassword"));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Refresh_Rotates_Token_And_Revokes_Old_One()
    {
        var db      = CreateDb();
        var service = MakeService(db);

        var initial = await service.RegisterAsync(MakeRegisterRequest());
        var oldToken = initial.RefreshToken;

        var refreshed = await service.RefreshAsync(oldToken);

        // New token pair issued
        refreshed.AccessToken.Should().NotBeNullOrWhiteSpace();
        refreshed.RefreshToken.Should().NotBe(oldToken);

        // Old token is revoked — using it again should throw
        var act = async () => await service.RefreshAsync(oldToken);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Register_Enforces_Invite_Code_When_Enabled()
    {
        var db = CreateDb();
        db.PlatformSettings.AddRange(
            new PlatformSetting { Key = "inviteCodeRequired", Value = "true" },
            new PlatformSetting { Key = "inviteCode",         Value = "SECRETCODE" });
        await db.SaveChangesAsync();

        var service = MakeService(db);

        // No invite code — should throw
        var noCode = async () =>
            await service.RegisterAsync(
                new RegisterRequest("Test", "a@test.ee", "Pass1234", "Pass1234", null, "et"));
        await noCode.Should().ThrowAsync<ArgumentException>();

        // Wrong invite code — should throw
        var wrongCode = async () =>
            await service.RegisterAsync(
                new RegisterRequest("Test", "a@test.ee", "Pass1234", "Pass1234", "WRONG", "et"));
        await wrongCode.Should().ThrowAsync<ArgumentException>();

        // Correct invite code — should succeed
        var response = await service.RegisterAsync(
            new RegisterRequest("Test", "a@test.ee", "Pass1234", "Pass1234", "SECRETCODE", "et"));
        response.User.Email.Should().Be("a@test.ee");
    }
}
