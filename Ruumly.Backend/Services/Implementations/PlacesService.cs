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
        var cached   = await cache.GetStringAsync(cacheKey, ct);
        if (cached is not null)
            return JsonSerializer.Deserialize<GooglePlaceSummaryDto>(cached);

        var url = "https://maps.googleapis.com/maps/api/place/details/json" +
                  $"?place_id={Uri.EscapeDataString(placeId)}" +
                  "&fields=rating,user_ratings_total,reviews,url" +
                  $"&language={Uri.EscapeDataString(language)}" +
                  "&reviews_sort=most_relevant" +
                  $"&key={apiKey}";

        using var http     = httpFactory.CreateClient();
        using var response = await http.GetAsync(url, ct);
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
}
