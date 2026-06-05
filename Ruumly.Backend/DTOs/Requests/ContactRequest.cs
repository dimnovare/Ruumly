namespace Ruumly.Backend.DTOs.Requests;

public record ContactRequest(
    string Name,
    string Email,
    string Subject,
    string Message,
    string? Language = null
);
