namespace Ruumly.Backend.DTOs.Requests;

public record SendMessageRequest(
    Guid   BookingId,
    string Text
);
