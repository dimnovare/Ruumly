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
        // Check invite code if registration is restricted
        var inviteRequired = await db.PlatformSettings
            .FirstOrDefaultAsync(s => s.Key == "inviteCodeRequired");

        if (inviteRequired?.Value == "true")
        {
            var validCode = await db.PlatformSettings
                .FirstOrDefaultAsync(s => s.Key == "inviteCode");

            var expected = validCode?.Value ?? "";

            if (string.IsNullOrWhiteSpace(request.InviteCode) ||
                !string.Equals(
                    request.InviteCode.Trim(),
                    expected.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Vale kutse kood. Kui sul on kutse, " +
                    "kontrolli koodi õigsust.");
            }
        }

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
            Language     = request.Language is "en" or "ru" ? request.Language : "et",
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
                // New user via Google — check invite gate
                var inviteRequired = await db.PlatformSettings
                    .FirstOrDefaultAsync(s => s.Key == "inviteCodeRequired");

                if (inviteRequired?.Value == "true")
                    throw new UnauthorizedAccessException(
                        "Registreerimine on hetkel ainult kutsega. " +
                        "Võtke meiega ühendust: info@ruumly.eu");

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
        var t = EmailTranslations.For(user.Language);

        var textBody =
            $"{t.PasswordResetGreeting}\n\n" +
            $"{t.PasswordResetBody1} ({user.Email}).\n\n" +
            $"{t.PasswordResetBody2}\n{resetUrl}\n\n" +
            $"{t.PasswordResetSecurityTitle}:\n" +
            $"{t.PasswordResetSecurityBody} " +
            $"{t.PasswordResetContactUs}\n\n" +
            $"Ruumly\ninfo@ruumly.eu";

        var htmlBody = BuildPasswordResetHtml(t, user.Email, resetUrl);

        await emailSender.SendAsync(user.Email, t.PasswordResetSubject, textBody, htmlBody);
    }

    public async Task<bool> ResetPasswordAsync(string token, string newPassword)
    {
        if (newPassword.Length < 8) return false;

        var user = await db.Users
            .FirstOrDefaultAsync(u =>
                u.PasswordResetToken == token &&
                u.PasswordResetExpiry > DateTime.UtcNow);

        if (user is null) return false;

        // Prevent reusing the same password
        if (BC.Verify(newPassword, user.PasswordHash))
            throw new ArgumentException(
                "Uus parool peab erinema praegusest paroolist. " +
                "Palun valige erinev parool.");

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

    public async Task UpdateLanguageAsync(Guid userId, string language)
    {
        var user = await db.Users.FindAsync(userId)
            ?? throw new NotFoundException("Kasutajat ei leitud.");
        user.Language = language;
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

    private static string BuildPasswordResetHtml(
        EmailTranslations.EmailStrings t,
        string email,
        string resetUrl) =>
    $$"""
    <!DOCTYPE html>
    <html>
    <head>
      <meta charset="UTF-8">
      <meta name="viewport" content="width=device-width,initial-scale=1.0">
    </head>
    <body style="margin:0;padding:0;background:#f5f5f5;font-family:Arial,sans-serif;">
      <table width="100%" cellpadding="0" cellspacing="0"
        style="background:#f5f5f5;padding:32px 0;">
        <tr><td align="center">
          <table width="560" cellpadding="0" cellspacing="0"
            style="background:#fff;border-radius:8px;
            box-shadow:0 2px 8px rgba(0,0,0,0.08);">
            <tr>
              <td style="background:#00897B;padding:28px 40px;border-radius:8px 8px 0 0;">
                <h1 style="margin:0;color:#fff;font-size:22px;font-weight:700;">Ruumly</h1>
              </td>
            </tr>
            <tr>
              <td style="padding:36px 40px;">
                <p style="margin:0 0 8px;color:#455A64;font-size:15px;">
                  {{t.PasswordResetGreeting}}
                </p>
                <p style="margin:0 0 16px;color:#455A64;font-size:15px;line-height:1.6;">
                  {{t.PasswordResetBody1}} <strong>{{email}}</strong>.
                </p>
                <p style="margin:0 0 8px;color:#455A64;font-size:15px;line-height:1.6;">
                  {{t.PasswordResetBody2}}
                </p>
                <p style="margin:0 0 28px;color:#455A64;font-size:15px;line-height:1.6;">
                  {{t.PasswordResetExpiry}}
                </p>
                <table cellpadding="0" cellspacing="0" style="margin:0 0 32px;">
                  <tr>
                    <td style="background:#00897B;border-radius:6px;">
                      <a href="{{resetUrl}}"
                        style="display:inline-block;padding:14px 32px;color:#fff;
                        font-size:15px;font-weight:600;text-decoration:none;">
                        {{t.PasswordResetButton}}
                      </a>
                    </td>
                  </tr>
                </table>
                <p style="margin:0 0 4px;color:#455A64;font-size:13px;">
                  {{t.PasswordResetCopyLabel}}
                </p>
                <p style="margin:0 0 32px;color:#00897B;font-size:13px;word-break:break-all;">
                  {{resetUrl}}
                </p>
                <table width="100%" cellpadding="0" cellspacing="0"
                  style="background:#FFF8E1;border-left:4px solid #FF8F00;
                  border-radius:4px;margin-bottom:24px;">
                  <tr>
                    <td style="padding:16px 20px;">
                      <p style="margin:0 0 8px;color:#E65100;font-size:14px;font-weight:700;">
                        {{t.PasswordResetSecurityTitle}}
                      </p>
                      <p style="margin:0;color:#5D4037;font-size:13px;line-height:1.5;">
                        {{t.PasswordResetSecurityBody}}
                        <a href="mailto:{{t.PasswordResetContactUs}}" style="color:#00897B;">
                          {{t.PasswordResetContactUs}}
                        </a>
                      </p>
                    </td>
                  </tr>
                </table>
              </td>
            </tr>
            <tr>
              <td style="background:#F5F5F5;padding:20px 40px;
                border-top:1px solid #ECEFF1;border-radius:0 0 8px 8px;">
                <p style="margin:0;color:#90A4AE;font-size:12px;line-height:1.5;">
                  Ruumly · ruumly.eu<br>
                  {{t.PasswordResetFooter}}
                </p>
              </td>
            </tr>
          </table>
        </td></tr>
      </table>
    </body>
    </html>
    """;
}
