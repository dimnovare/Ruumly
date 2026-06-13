namespace Ruumly.Backend.DTOs.Responses;

public record GoogleReviewDto(
    string  AuthorName,
    string? AuthorPhotoUrl,
    int     Rating,               // 1–5
    string  Text,
    string  RelativeTimeDesc,     // "2 months ago"
    long    Time                  // Unix timestamp
);

public record GooglePlaceSummaryDto(
    decimal             Rating,
    int                 TotalRatings,
    string              MapsUrl,           // "https://g.page/..."
    List<GoogleReviewDto> Reviews
);

/// <summary>One Google Places autocomplete suggestion.</summary>
public record PlaceSuggestionDto(string PlaceId, string Description);

/// <summary>Prefill data resolved from a selected Google place.</summary>
public record PlacePrefillDto(
    string  Name,
    string  Address,
    string  City,
    double  Lat,
    double  Lng,
    string? OpeningHours,
    string? MapsUrl
);
