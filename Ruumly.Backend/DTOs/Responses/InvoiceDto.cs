namespace Ruumly.Backend.DTOs.Responses;

public record InvoiceDto(
    Guid    Id,
    Guid    BookingId,
    decimal Amount,
    string  Status,
    string  IssuedAt,
    string? PaidAt,
    string  Description
);
