using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class PlacesService(
    IDistributedCache   cache,
    IConfiguration      config,
    IHttpClientFactory  httpFactory) : IPlacesService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    // Snake_case names come from Google's JSON — use CamelCase ↔ snake_case policy.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<GooglePlaceSummaryDto?> GetReviewsAsync(
        string placeId, string language, CancellationToken ct = default)
    {
        var apiKey = config["GooglePlaces:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;   // feature disabled — no error surfaced to callers

        var cacheKey = $"places:{placeId}:{language}";
        var cached = await cache.GetStringAsync(cacheKey, ct);
        if (cached is not null)
        {
            try
            {
                var hit = JsonSerializer.Deserialize<GooglePlaceSummaryDto>(cached);
                if (hit is not null) return hit;
            }
            catch (JsonException)
            {
                await cache.RemoveAsync(cacheKey, ct);
            }
        }

        var url = "https://maps.googleapis.com/maps/api/place/details/json" +
                  $"?place_id={Uri.EscapeDataString(placeId)}" +
                  "&fields=rating,user_ratings_total,reviews,url" +
                  $"&language={Uri.EscapeDataString(language)}" +
                  "&reviews_sort=most_relevant";

        using var http    = httpFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Goog-Api-Key", apiKey);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(ct);
        var root = JsonSerializer.Deserialize<PlacesApiResponse>(json, JsonOpts);

        if (root?.Status != "OK" || root.Result is null) return null;

        var result  = root.Result;
        var reviews = (result.Reviews ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r.Text) && r.Rating >= 1)
            .Take(5)
            .Select(r => new GoogleReviewDto(
                AuthorName:      r.AuthorName             ?? string.Empty,
                AuthorPhotoUrl:  r.ProfilePhotoUrl,
                Rating:          r.Rating,
                Text:            r.Text                   ?? string.Empty,
                RelativeTimeDesc: r.RelativeTimeDescription ?? string.Empty,
                Time:            r.Time))
            .ToList();

        var dto = new GooglePlaceSummaryDto(
            Rating:       (decimal)(result.Rating ?? 0),
            TotalRatings: result.UserRatingsTotal  ?? 0,
            MapsUrl:      result.Url               ?? string.Empty,
            Reviews:      reviews);

        await cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(dto),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
            ct);

        return dto;
    }

    public async Task<IReadOnlyList<PlaceSuggestionDto>> AutocompleteAsync(
        string input, string language, CancellationToken ct = default)
    {
        var apiKey = config["GooglePlaces:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(input) || input.Trim().Length < 3)
            return [];

        var url = "https://maps.googleapis.com/maps/api/place/autocomplete/json" +
                  $"?input={Uri.EscapeDataString(input.Trim())}" +
                  $"&language={Uri.EscapeDataString(language)}" +
                  "&components=country:ee|country:lv|country:lt";

        using var http    = httpFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Goog-Api-Key", apiKey);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return [];

        var json = await response.Content.ReadAsStringAsync(ct);
        var root = JsonSerializer.Deserialize<AutocompleteResponse>(json, JsonOpts);
        if (root?.Predictions is null) return [];

        return root.Predictions
            .Where(p => !string.IsNullOrWhiteSpace(p.PlaceId))
            .Take(8)
            .Select(p => new PlaceSuggestionDto(p.PlaceId!, p.Description ?? string.Empty))
            .ToList();
    }

    public async Task<PlacePrefillDto?> GetPrefillAsync(
        string placeId, string language, CancellationToken ct = default)
    {
        var apiKey = config["GooglePlaces:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(placeId))
            return null;

        var url = "https://maps.googleapis.com/maps/api/place/details/json" +
                  $"?place_id={Uri.EscapeDataString(placeId)}" +
                  "&fields=name,formatted_address,geometry,address_component,opening_hours,url" +
                  $"&language={Uri.EscapeDataString(language)}";

        using var http    = httpFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Goog-Api-Key", apiKey);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(ct);
        var root = JsonSerializer.Deserialize<DetailsResponse>(json, JsonOpts);
        if (root?.Status != "OK" || root.Result is null) return null;

        var r = root.Result;

        // City: prefer locality, then postal_town, then admin_area_level_2.
        string? city = null;
        foreach (var comp in r.AddressComponents ?? [])
        {
            var types = comp.Types ?? [];
            if (types.Contains("locality")) { city = comp.LongName; break; }
            if (city is null && types.Contains("postal_town"))                 city = comp.LongName;
            if (city is null && types.Contains("administrative_area_level_2")) city = comp.LongName;
        }

        var hours = r.OpeningHours?.WeekdayText is { Count: > 0 } wt
            ? string.Join("; ", wt)
            : null;

        return new PlacePrefillDto(
            Name:         r.Name             ?? string.Empty,
            Address:      r.FormattedAddress ?? string.Empty,
            City:         city               ?? string.Empty,
            Lat:          r.Geometry?.Location?.Lat ?? 0,
            Lng:          r.Geometry?.Location?.Lng ?? 0,
            OpeningHours: hours,
            MapsUrl:      r.Url);
    }

    // ── Internal deserialization models (Google Places Details API) ───────────
    // These never leave this file; the public DTOs above are what callers consume.

    private sealed class PlacesApiResponse
    {
        public string?       Status { get; set; }
        public PlacesResult? Result { get; set; }
    }

    private sealed class PlacesResult
    {
        public double?            Rating            { get; set; }
        public int?               UserRatingsTotal  { get; set; }
        public string?            Url               { get; set; }
        public List<PlacesReview>? Reviews          { get; set; }
    }

    private sealed class PlacesReview
    {
        public string? AuthorName              { get; set; }
        public string? ProfilePhotoUrl         { get; set; }
        public int     Rating                  { get; set; }
        public string? Text                    { get; set; }
        public string? RelativeTimeDescription { get; set; }
        public long    Time                    { get; set; }
    }

    private sealed class AutocompleteResponse
    {
        public string?                       Status      { get; set; }
        public List<AutocompletePrediction>? Predictions { get; set; }
    }

    private sealed class AutocompletePrediction
    {
        public string? PlaceId     { get; set; }
        public string? Description { get; set; }
    }

    private sealed class DetailsResponse
    {
        public string?        Status { get; set; }
        public DetailsResult? Result { get; set; }
    }

    private sealed class DetailsResult
    {
        public string?                        Name              { get; set; }
        public string?                        FormattedAddress  { get; set; }
        public DetailsGeometry?               Geometry          { get; set; }
        public List<DetailsAddressComponent>? AddressComponents { get; set; }
        public DetailsOpeningHours?           OpeningHours      { get; set; }
        public string?                        Url               { get; set; }
    }

    private sealed class DetailsGeometry
    {
        public DetailsLocation? Location { get; set; }
    }

    private sealed class DetailsLocation
    {
        public double Lat { get; set; }
        public double Lng { get; set; }
    }

    private sealed class DetailsAddressComponent
    {
        public string?       LongName  { get; set; }
        public string?       ShortName { get; set; }
        public List<string>? Types     { get; set; }
    }

    private sealed class DetailsOpeningHours
    {
        public List<string>? WeekdayText { get; set; }
    }
}
