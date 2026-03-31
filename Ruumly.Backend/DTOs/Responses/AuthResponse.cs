namespace Ruumly.Backend.DTOs.Responses;

public record AuthResponse(
    UserDto User,
    string AccessToken,
    string RefreshToken,
    string? CsrfToken = null   // included in body so the frontend can store it in-memory
);
