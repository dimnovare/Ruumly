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

    private static AuthService MakeService(RuumlyDbContext db, IEmailSender? emailSender = null) =>
        new(db, MakeConfig(), emailSender ?? new NoOpEmailSender(), new NoOpHttpContextAccessor(),
            NullLogger<AuthService>.Instance);

    private sealed class NoOpEmailSender : IEmailSender
    {
        public Task SendAsync(string to, string subject, string textBody, string? htmlBody = null)
            => Task.CompletedTask;
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public string? TextBody { get; private set; }
        public string? HtmlBody { get; private set; }

        public Task SendAsync(string to, string subject, string textBody, string? htmlBody = null)
        {
            TextBody = textBody;
            HtmlBody = htmlBody;
            return Task.CompletedTask;
        }
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
    public async Task Register_Sends_Verification_Link_With_User_Language_Prefix()
    {
        var db = CreateDb();
        var email = new CapturingEmailSender();
        var service = MakeService(db, email);

        await service.RegisterAsync(
            new RegisterRequest("Test", "test-en@ruumly.ee", "Password123",
                "Password123", null, "en"));

        email.TextBody.Should().Contain("https://test.ruumly.eu/en/verify?token=");
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

        // The old token is rotation-revoked. Replaying it OUTSIDE the reuse-grace must
        // throw. (Within the 10s grace it would be honoured as a concurrent-refresh
        // replay — covered separately by the reuse-grace tests below.) Backdate the
        // revocation past the grace window so this asserts the hard-rejection path.
        var oldHash = HashTokenForTest(oldToken);
        var predecessor = await db.RefreshTokens.SingleAsync(t => t.TokenHash == oldHash);
        predecessor.RevokedAt = DateTime.UtcNow.AddSeconds(-11);
        await db.SaveChangesAsync();

        var act = async () => await service.RefreshAsync(oldToken);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    // ─── Refresh-token reuse-grace (multi-tab rotation race) ────────────────

    // Helper: rotate once, then look up the now-revoked predecessor token row so the
    // test can adjust its RevokedAt to simulate "n seconds ago".
    private static async Task<RefreshToken> RotatedPredecessor(
        RuumlyDbContext db, AuthService service, string oldRawToken)
    {
        await service.RefreshAsync(oldRawToken);
        var oldHash = HashTokenForTest(oldRawToken);
        return await db.RefreshTokens.SingleAsync(t => t.TokenHash == oldHash);
    }

    private static string HashTokenForTest(string token) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    [Fact]
    public async Task Refresh_Within_Grace_Accepts_Rotation_Revoked_Token_Without_Reissuing_Cookie()
    {
        var db      = CreateDb();
        var service = MakeService(db);

        var initial  = await service.RegisterAsync(MakeRegisterRequest());
        var oldToken = initial.RefreshToken;

        // First refresh rotates the cookie (predecessor becomes rotation-revoked).
        var firstRefresh = await service.RefreshAsync(oldToken);
        firstRefresh.RefreshToken.Should().NotBeNullOrWhiteSpace();
        firstRefresh.RefreshToken.Should().NotBe(oldToken);

        // Confirm the predecessor was revoked BY ROTATION (ReplacedByTokenId set, RevokedAt recent).
        var predecessor = await db.RefreshTokens.SingleAsync(t => t.TokenHash == HashTokenForTest(oldToken));
        predecessor.IsRevoked.Should().BeTrue();
        predecessor.RevokedAt.Should().NotBeNull();
        predecessor.ReplacedByTokenId.Should().NotBeNull();

        // Second tab replays the SAME old cookie within the grace window.
        var graceReplay = await service.RefreshAsync(oldToken);

        // Gets a valid, fresh access token for the same user...
        graceReplay.AccessToken.Should().NotBeNullOrWhiteSpace();
        ReadSubClaim(graceReplay.AccessToken).Should().Be(initial.User.Id.ToString());
        graceReplay.CsrfToken.Should().NotBeNullOrWhiteSpace();

        // ...but NO new refresh token → controller skips Set-Cookie, the already-rotated cookie stays intact.
        graceReplay.RefreshToken.Should().BeEmpty();

        // And the grace path must NOT have rotated again (no extra active token minted).
        var activeCount = await db.RefreshTokens.CountAsync(t => !t.IsRevoked);
        activeCount.Should().Be(1);
    }

    [Fact]
    public async Task Refresh_After_Grace_Window_Rejects_Rotation_Revoked_Token()
    {
        var db      = CreateDb();
        var service = MakeService(db);

        var initial  = await service.RegisterAsync(MakeRegisterRequest());
        var oldToken = initial.RefreshToken;

        var predecessor = await RotatedPredecessor(db, service, oldToken);

        // Backdate the revocation to 11s ago — outside the 10s grace.
        predecessor.RevokedAt = DateTime.UtcNow.AddSeconds(-11);
        await db.SaveChangesAsync();

        var act = async () => await service.RefreshAsync(oldToken);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Refresh_Rejects_Logout_Revoked_Token_Even_Within_Grace_Window()
    {
        var db      = CreateDb();
        var service = MakeService(db);

        var initial  = await service.RegisterAsync(MakeRegisterRequest());
        var token    = initial.RefreshToken;

        // Logout revokes WITHOUT rotation → ReplacedByTokenId stays null.
        await service.LogoutAsync(token);

        var revoked = await db.RefreshTokens.SingleAsync(t => t.TokenHash == HashTokenForTest(token));
        revoked.IsRevoked.Should().BeTrue();
        revoked.RevokedAt.Should().NotBeNull();
        revoked.ReplacedByTokenId.Should().BeNull();  // distinguishes it from a rotation-revoke

        // Even though RevokedAt is well inside the 10s window, a logout-killed token must stay rejected.
        var act = async () => await service.RefreshAsync(token);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    // Decodes the `sub` (subject = user id) claim from a JWT without signature validation.
    private static string ReadSubClaim(string jwt)
    {
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var token   = handler.ReadJwtToken(jwt);
        return token.Subject;
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

    [Fact]
    public async Task RequestPasswordReset_Sends_Login_Link_With_User_Language_Prefix()
    {
        var db = CreateDb();
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = "reset@ruumly.ee",
            Name = "Reset User",
            PasswordHash = BC.HashPassword("Password123", workFactor: 4),
            Role = UserRole.Customer,
            Status = UserStatus.Active,
            Language = "lv",
        });
        await db.SaveChangesAsync();

        var email = new CapturingEmailSender();
        var service = MakeService(db, email);

        await service.RequestPasswordResetAsync("reset@ruumly.ee");

        email.TextBody.Should().Contain("https://test.ruumly.eu/lv/login?view=reset&token=");
        email.HtmlBody.Should().Contain("https://test.ruumly.eu/lv/login?view=reset&token=");
    }

    [Fact]
    public async Task ResetPassword_Allows_Passwordless_Public_Applicant_To_Set_First_Password()
    {
        var db = CreateDb();
        const string rawToken = "first-password-token";
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = "applicant@ruumly.ee",
            Name = "Public Applicant",
            PasswordHash = string.Empty,
            PasswordResetToken = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant(),
            PasswordResetExpiry = DateTime.UtcNow.AddHours(1),
            Role = UserRole.Customer,
            Status = UserStatus.Active,
            Language = "en",
        });
        await db.SaveChangesAsync();

        var service = MakeService(db);

        var success = await service.ResetPasswordAsync(rawToken, "demo1234");

        success.Should().BeTrue();
        var user = await db.Users.SingleAsync();
        BC.Verify("demo1234", user.PasswordHash).Should().BeTrue();
        user.PasswordResetToken.Should().BeNull();
        user.PasswordResetExpiry.Should().BeNull();
    }
}
