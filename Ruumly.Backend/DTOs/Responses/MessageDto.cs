namespace Ruumly.Backend.DTOs.Responses;

public record MessageDto(
    Guid   Id,
    Guid   BookingId,
    string From,
    string SenderName,
    string Text,
    bool   Read,
    string CreatedAt
);
