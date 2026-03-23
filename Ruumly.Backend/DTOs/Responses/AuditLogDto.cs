namespace Ruumly.Backend.DTOs.Responses;

public record AuditLogDto(
    Guid    Id,
    string  Action,
    string  Actor,
    string  Target,
    string? Detail,
    string  CreatedAt
);
