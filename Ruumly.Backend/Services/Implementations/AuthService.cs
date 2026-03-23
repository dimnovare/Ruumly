using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Services.Interfaces;
using BC = BCrypt.Net.BCrypt;
using Google.Apis.Auth;
// IEmailSender is in Ruumly.Backend.Services.Interfaces namespace

namespace Ruumly.Backend.Services.Implementations;

public class AuthService(RuumlyDbContext db, IConfiguration config, IEmailSender emailSender) : IAuthService
{
    // ─── Public methods ───────────────────────────────────────────────────────

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        var exists = await db.Users.AnyAsync(u =>
            u.Email.ToLower() == request.Email.ToLower());

        if (exists)
            throw new ConflictException("Email already registered");

        var user = new User
        {
            Id           = Guid.NewGuid(),
            Name         = request.Name.Trim(),
            Email        = request.Email.ToLower().Trim(),
            PasswordHash = BC.HashPassword(request.Password, workFactor: 12),
            Role         = UserRole.Customer,
            Status       = UserStatus.Active,
            RegisteredAt = DateTime.UtcNow,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return await GenerateAuthResponseAsync(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());

        // Verify only when user exists; short-circuit still leaks timing but avoids BCrypt exception on null hash
        var passwordOk = user is not null && BC.Verify(request.Password, user.PasswordHash);
        if (!passwordOk || user is null)
            throw new UnauthorizedAccessException("Invalid email or password");

        if (user.Status == UserStatus.Blocked)
            throw new ForbiddenException("Account is blocked");

        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return await GenerateAuthResponseAsync(user);
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken)
    {
        var tokenHash = HashToken(refreshToken);

        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash)
            ?? throw new UnauthorizedAccessException("Invalid refresh token");

        if (stored.IsRevoked || stored.ExpiresAt < DateTime.UtcNow)
            throw new UnauthorizedAccessException("Refresh token has expired or been revoked");

        stored.IsRevoked = true;
        await db.SaveChangesAsync();

        return await GenerateAuthResponseAsync(stored.User);
    }

    public async Task LogoutAsync(string refreshToken)
    {
        var tokenHash = HashToken(refreshToken);

        var stored = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        if (stored is null) return;

        stored.IsRevoked = true;
        await db.SaveChangesAsync();
    }

    public async Task<UserDto> GetMeAsync(Guid userId)
    {
        var user = await db.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        return MapToDto(user);
    }

    public async Task<AuthResponse> GoogleLoginAsync(string credential)
    {
        // 1. Verify the Google ID token
        GoogleJsonWebSignature.Payload payload;
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { config["Google:ClientId"]! }
            };
            payload = await GoogleJsonWebSignature.ValidateAsync(credential, settings);
        }
        catch (InvalidJwtException ex)
        {
            throw new UnauthorizedAccessException($"Invalid Google token: {ex.Message}");
        }

        // 2. Find existing user by Google ID first, then by email
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.GoogleId == payload.Subject);

        if (user is null)
        {
            // Try to find by email (user may have registered with password first)
            user = await db.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == payload.Email.ToLower());

            if (user is not null)
            {
                // Link Google ID to existing email account
                user.GoogleId = payload.Subject;
                if (string.IsNullOrWhiteSpace(user.Avatar) &&
                    !string.IsNullOrWhiteSpace(payload.Picture))
                    user.Avatar = payload.Picture;
            }
            else
            {
                // Create brand new user
                user = new User
                {
                    Id           = Guid.NewGuid(),
                    Name         = payload.Name ?? payload.Email,
                    Email        = payload.Email.ToLower(),
                    PasswordHash = BC.HashPassword(Guid.NewGuid().ToString(), workFactor: 4),
                    Role         = UserRole.Customer,
                    Status       = UserStatus.Active,
                    GoogleId     = payload.Subject,
                    Avatar       = payload.Picture,
                    RegisteredAt = DateTime.UtcNow,
                };
                db.Users.Add(user);
            }

            await db.SaveChangesAsync();
        }

        // 3. Check account is not blocked
        if (user.Status == UserStatus.Blocked)
            throw new UnauthorizedAccessException(
                "Konto on blokeeritud. Võtke ühendust toega.");

        // 4. Update last login
        user.LastLoginAt = DateTime.UtcNow;

        // 5. Issue Ruumly JWT pair (same as email login)
        var accessToken  = GenerateJwt(user);
        var refreshToken = GenerateRawRefreshToken();
        var tokenHash    = HashToken(refreshToken);

        db.RefreshTokens.Add(new RefreshToken
        {
            Id        = Guid.NewGuid(),
            UserId    = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(
                int.Parse(config["Jwt:RefreshTokenExpiryDays"] ?? "7")),
            IsRevoked = false,
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();

        return new AuthResponse(
            User:         MapToDto(user),
            AccessToken:  accessToken,
            RefreshToken: refreshToken
        );
    }

    public async Task RequestPasswordResetAsync(string email)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());

        // Always return — never reveal whether email exists
        if (user is null) return;

        var token  = Convert.ToHexString(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        user.PasswordResetToken  = token;
        user.PasswordResetExpiry = DateTime.UtcNow.AddHours(2);
        await db.SaveChangesAsync();

        var resetUrl = $"{config["AppUrl"]}/login?view=reset&token={token}";
        await emailSender.SendAsync(
            user.Email,
            "Ruumly — paroolivahetus",
            $"Parooli vahetamiseks kliki: {resetUrl}\n\n" +
            $"Link kehtib 2 tundi. Kui sa ei küsinud parooli vahetust, " +
            $"ignoreeri seda emaili.",
            $"<p>Parooli vahetamiseks <a href=\"{resetUrl}\">kliki siia</a>.</p>" +
            $"<p>Link kehtib 2 tundi.</p>"
        );
    }

    public async Task<bool> ResetPasswordAsync(string token, string newPassword)
    {
        if (newPassword.Length < 8) return false;

        var user = await db.Users
            .FirstOrDefaultAsync(u =>
                u.PasswordResetToken == token &&
                u.PasswordResetExpiry > DateTime.UtcNow);

        if (user is null) return false;

        user.PasswordHash        = BC.HashPassword(newPassword, workFactor: 12);
        user.PasswordResetToken  = null;
        user.PasswordResetExpiry = null;

        // Revoke all refresh tokens for security
        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == user.Id)
            .ToListAsync();
        foreach (var t in tokens) t.IsRevoked = true;

        await db.SaveChangesAsync();
        return true;
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private async Task<AuthResponse> GenerateAuthResponseAsync(User user)
    {
        var accessToken   = GenerateJwt(user);
        var refreshToken  = GenerateRawRefreshToken();
        var tokenHash     = HashToken(refreshToken);
        var expiryDays    = int.Parse(config["Jwt:RefreshTokenExpiryDays"]!);

        db.RefreshTokens.Add(new RefreshToken
        {
            Id        = Guid.NewGuid(),
            UserId    = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(expiryDays),
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();

        return new AuthResponse(MapToDto(user), accessToken, refreshToken);
    }

    private string GenerateJwt(User user)
    {
        var key     = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Secret"]!));
        var creds   = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(int.Parse(config["Jwt:AccessTokenExpiryMinutes"]!));

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub,   user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role,               user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer:             config["Jwt:Issuer"],
            audience:           config["Jwt:Audience"],
            claims:             claims,
            expires:            expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateRawRefreshToken()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        return Guid.NewGuid().ToString("N") + Convert.ToBase64String(randomBytes);
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLower();
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        if (request.NewPassword != request.ConfirmPassword)
            throw new ArgumentException("Uus parool ja kinnitus ei ühti.");

        if (request.NewPassword.Length < 8)
            throw new ArgumentException("Parool peab olema vähemalt 8 tähemärki pikk.");

        var user = await db.Users.FindAsync(userId)
            ?? throw new NotFoundException("Kasutajat ei leitud.");

        // For Google-only users who have no real password,
        // allow setting a new password without verifying old one
        bool isGoogleOnly = user.GoogleId is not null &&
            string.IsNullOrWhiteSpace(request.CurrentPassword);

        if (!isGoogleOnly)
        {
            if (string.IsNullOrWhiteSpace(request.CurrentPassword))
                throw new ArgumentException("Praegune parool on kohustuslik.");

            if (!BC.Verify(request.CurrentPassword, user.PasswordHash))
                throw new ArgumentException("Praegune parool on vale.");
        }

        user.PasswordHash = BC.HashPassword(request.NewPassword, workFactor: 12);

        // Revoke all refresh tokens — require re-login on all devices
        await db.RefreshTokens
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(t => t.IsRevoked, true));

        await db.SaveChangesAsync();
    }

    private static UserDto MapToDto(User user) => new(
        user.Id,
        user.Name,
        user.Email,
        user.Role,
        user.Status,
        user.Company,
        user.Phone,
        user.Avatar,
        user.RegisteredAt,
        user.LastLoginAt,
        user.BookingsCount,
        HasGoogleAccount: user.GoogleId is not null
    );
}
