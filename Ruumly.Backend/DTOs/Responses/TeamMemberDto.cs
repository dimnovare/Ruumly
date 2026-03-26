namespace Ruumly.Backend.DTOs.Responses;

public record TeamMemberDto(
    Guid    Id,
    string  Name,
    string  Email,
    string? Phone,
    string  Role,
    bool    IsOwner,
    string? LastLoginAt,
    string  RegisteredAt);
