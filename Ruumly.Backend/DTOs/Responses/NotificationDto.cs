namespace Ruumly.Backend.DTOs.Responses;

public record NotificationDto(
    Guid    Id,
    string  Type,
    string  Title,
    string  Desc,
    bool    Read,
    string  Time,
    string? ActionUrl,
    string? EntityId,
    string? EntityType
);
