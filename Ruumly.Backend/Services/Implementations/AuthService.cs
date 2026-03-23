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

namespace Ruumly.Backend.Services.Implementations;

public class AuthService(RuumlyDbContext db, IConfiguration config) : IAuthService
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
        user.BookingsCount
    );
}
