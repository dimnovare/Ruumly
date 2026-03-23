namespace Ruumly.Backend.DTOs.Responses;

public record BookingTimelineDto(
    string Date,
    string Event,
    string Status
);

public record OrderSummaryDto(
    Guid Id,
    string Status,
    string IntegrationType,
    string SupplierName,
    decimal SupplierPrice,
    decimal ExtrasTotal,
    decimal Total,
    decimal Margin,
    string CreatedAt,
    string? SentAt
);

public record BookingDto(
    Guid Id,
    Guid ListingId,
    string ListingTitle,
    string ListingType,
    string Provider,
    string City,
    string StartDate,
    string? EndDate,
    string Duration,
    string Status,
    List<string> Extras,
    decimal BasePrice,
    decimal PlatformPrice,
    decimal ExtrasTotal,
    decimal Total,
    string CreatedAt,
    List<BookingTimelineDto> Timeline,
    OrderSummaryDto? Order
);
