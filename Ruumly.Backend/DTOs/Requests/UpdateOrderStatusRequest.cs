namespace Ruumly.Backend.DTOs.Requests;

public record UpdateOrderStatusRequest(
    string  Status,
    string? Notes,
    string? ApprovedBy
);
