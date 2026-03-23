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
}
