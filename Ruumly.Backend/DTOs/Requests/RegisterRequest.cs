namespace Ruumly.Backend.DTOs.Requests;

public record RegisterRequest(
    string Name,
    string Email,
    string Password,
    string ConfirmPassword
);
