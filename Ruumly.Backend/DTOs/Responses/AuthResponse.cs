namespace Ruumly.Backend.DTOs.Responses;

public record AuthResponse(
    UserDto User,
    string AccessToken,
    string RefreshToken
);
