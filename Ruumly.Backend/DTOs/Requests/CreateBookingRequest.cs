namespace Ruumly.Backend.DTOs.Requests;

public record CreateBookingRequest
{
    public Guid ListingId { get; init; }
    public string StartDate { get; init; } = string.Empty;
    public string? EndDate { get; init; }
    public string Duration { get; init; } = string.Empty;
    public List<string> Extras { get; init; } = [];
    public string ContactName { get; init; } = string.Empty;
    public string ContactEmail { get; init; } = string.Empty;
    public string ContactPhone { get; init; } = string.Empty;
    public string? Notes { get; init; }
    public string PaymentMethod { get; init; } = string.Empty;
}
