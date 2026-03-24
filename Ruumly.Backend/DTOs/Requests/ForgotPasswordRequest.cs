namespace Ruumly.Backend.DTOs.Requests;

public record ForgotPasswordRequest(string Email, string? Language);
