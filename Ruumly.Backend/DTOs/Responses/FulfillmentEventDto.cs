namespace Ruumly.Backend.DTOs.Responses;

public record FulfillmentEventDto(
    Guid    Id,
    string  Status,
    string  Actor,
    string? Channel,
    string? Detail,
    string  CreatedAt
);
