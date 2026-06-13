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

    /// <summary>
    /// Google Places autocomplete (Baltics-restricted) for a typed query.
    /// Returns an empty list when the feature is disabled (no API key), the
    /// query is too short, or the call fails.
    /// </summary>
    Task<IReadOnlyList<PlaceSuggestionDto>> AutocompleteAsync(
        string input,
        string language,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves a selected place into prefill data (name, address, city, lat/lng,
    /// opening hours). Returns null when disabled or the call fails.
    /// </summary>
    Task<PlacePrefillDto?> GetPrefillAsync(
        string placeId,
        string language,
        CancellationToken ct = default);
}
