using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;

namespace Ruumly.Backend.Services.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<AuthResponse> RefreshAsync(string refreshToken);
    Task LogoutAsync(string refreshToken);
    Task<UserDto> GetMeAsync(Guid userId);
    Task RequestPasswordResetAsync(string email);
    Task<bool> ResetPasswordAsync(string token, string newPassword);

    /// <summary>
    /// Verifies a Google ID token, then finds or creates the user,
    /// and returns Ruumly JWT credentials.
    /// </summary>
    Task<AuthResponse> GoogleLoginAsync(string credential);

    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request);
    Task UpdateLanguageAsync(Guid userId, string language);
    Task<bool> VerifyEmailAsync(string token);
    Task ResendVerificationEmailAsync(Guid userId);

    /// <summary>
    /// Computes an HMAC-SHA256 of the raw refresh token using the JWT secret.
    /// Used to generate and validate the CSRF token that pairs with each refresh token.
    /// Deterministic — no DB storage needed.
    /// </summary>
    string ComputeCsrfToken(string rawRefreshToken);
}
