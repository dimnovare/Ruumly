namespace Ruumly.Backend.DTOs.Requests;

public record LoginRequest(
    string Email,
    string Password
);
