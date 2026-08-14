using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// The team invite is the second path that mints a Users row from an address a
/// human typed. Users.Email is the login identity, so it has to land in the same
/// canonical form the rest of the codebase stores — otherwise one mailbox owns
/// two accounts and the login lookup picks between them arbitrarily.
/// </summary>
public class SupplierTeamInviteTests
{
    // ─── Test infrastructure ───────────────────────────────────────────────

    private sealed class RecordingAuthService : IAuthService
    {
        public List<string> PasswordResets { get; } = [];

        public Task RequestPasswordResetAsync(string email)
        {
            PasswordResets.Add(email);
            return Task.CompletedTask;
        }

        public Task<AuthResponse> RegisterAsync(RegisterRequest request) => Task.FromResult<AuthResponse>(null!);
        public Task<AuthResponse> LoginAsync(LoginRequest request) => Task.FromResult<AuthResponse>(null!);
        public Task<AuthResponse> RefreshAsync(string refreshToken) => Task.FromResult<AuthResponse>(null!);
        public Task LogoutAsync(string refreshToken) => Task.CompletedTask;
        public Task<UserDto> GetMeAsync(Guid userId) => Task.FromResult<UserDto>(null!);
        public Task<bool> ResetPasswordAsync(string token, string newPassword) => Task.FromResult(false);
        public Task<AuthResponse> GoogleLoginAsync(string credential) => Task.FromResult<AuthResponse>(null!);
        public Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request) => Task.CompletedTask;
        public Task UpdateLanguageAsync(Guid userId, string language) => Task.CompletedTask;
        public Task<bool> VerifyEmailAsync(string token) => Task.FromResult(false);
        public Task ResendVerificationEmailAsync(Guid userId) => Task.CompletedTask;
        public string ComputeCsrfToken(string rawRefreshToken) => string.Empty;
    }

    private sealed class UnusedPricing : IPricingConfigService
    {
        public Task<PricingConfig> GetAsync() => throw new NotImplementedException();
        public Task InvalidateCacheAsync() => Task.CompletedTask;
        public Task<EffectivePricing> GetEffectivePricingAsync(Supplier supplier) =>
            throw new NotImplementedException();
    }

    /// <summary>Seeds a supplier plus its owner, and returns a controller acting as that owner.</summary>
    private static SupplierTeamController MakeController(
        RuumlyDbContext db, IAuthService auth, out Guid supplierId)
    {
        supplierId = Guid.NewGuid();
        db.Suppliers.Add(new Supplier
        {
            Id           = supplierId,
            Name         = "Kolimisabi OÜ",
            ContactName  = "Owner",
            ContactEmail = "info@kolimisabi.ee",
            IsActive     = true,
        });

        var ownerId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id           = ownerId,
            Name         = "Owner",
            Email        = "info@kolimisabi.ee",
            PasswordHash = "x",
            Role         = UserRole.Provider,
            Status       = UserStatus.Active,
            SupplierId   = supplierId,
            RegisteredAt = DateTime.UtcNow,
        });
        db.SaveChanges();

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, ownerId.ToString())], "test")),
        };

        return new SupplierTeamController(db, auth, new UnusedPricing())
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    // ─── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task InviteMember_StoresANewMembersAddressLowercased()
    {
        var db         = TestDbContext.Create();
        var auth       = new RecordingAuthService();
        var controller = MakeController(db, auth, out _);

        var result = await controller.InviteMember(
            new InviteTeamMemberRequest { Email = " Mari@Kolimisabi.EE ", Name = "Mari" });

        result.Should().BeOfType<OkObjectResult>();
        db.Users.Single(u => u.Name == "Mari").Email.Should().Be("mari@kolimisabi.ee");
        auth.PasswordResets.Should().ContainSingle(
            "the reset link has to reach the address the account was actually stored under")
            .Which.Should().Be("mari@kolimisabi.ee");
    }

    [Fact]
    public async Task InviteMember_ExistingAccountInDifferentCapitalisation_IsLinkedNotDuplicated()
    {
        // Inviting someone who already has a Ruumly account, typed with different
        // capitalisation, used to miss them and mint a SECOND row for the same
        // mailbox — leaving them with two logins and one working password.
        var db   = TestDbContext.Create();
        var auth = new RecordingAuthService();
        var controller = MakeController(db, auth, out var supplierId);

        db.Users.Add(new User
        {
            Id           = Guid.NewGuid(),
            Name         = "Mari",
            Email        = "Mari@Kolimisabi.ee",
            PasswordHash = "x",
            Role         = UserRole.Customer,
            Status       = UserStatus.Active,
            RegisteredAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await controller.InviteMember(
            new InviteTeamMemberRequest { Email = "mari@kolimisabi.ee", Name = "Mari" });

        result.Should().BeOfType<OkObjectResult>();
        db.Users.Count(u => u.Name == "Mari").Should().Be(1,
            "one mailbox is one account, however it was capitalised");

        var mari = db.Users.Single(u => u.Name == "Mari");
        mari.SupplierId.Should().Be(supplierId);
        mari.Role.Should().Be(UserRole.Provider);
    }
}
