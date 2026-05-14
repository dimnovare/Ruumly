using Ruumly.Backend.DTOs.Responses;

namespace Ruumly.Backend.Services.Interfaces;

public interface IPlacesService
{
    /// <summary>
    /// Returns a Google Places summary (rating + up to 5 reviews) for the given
    /// <paramref name="placeId"/> in the requested <paramref name="language"/>.
    /// Returns null when the feature is disabled (no API key) or the call fails.
    /// </summary>
    Task<GooglePlaceSummaryDto?> GetReviewsAsync(
        string placeId,
        string language,
        CancellationToken ct = default);
}
