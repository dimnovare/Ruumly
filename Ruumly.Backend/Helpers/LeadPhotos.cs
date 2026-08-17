using System.Text.Json;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// Reading <see cref="Models.DemandLead.PhotoKeysJson"/> back.
///
/// One place, because the column is JSON in a text field and every caller that
/// parsed it itself would be a caller that could throw on a malformed value.
/// Photos are an optional extra on a request — a bad parse must never be able
/// to take down the outreach email or the quote page that carry the request
/// itself.
/// </summary>
public static class LeadPhotos
{
    /// <summary>Storage keys for a lead, or empty. Never throws.</summary>
    public static IReadOnlyList<string> Keys(string? photoKeysJson)
    {
        if (string.IsNullOrWhiteSpace(photoKeysJson)) return [];

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(photoKeysJson);
            if (parsed is null) return [];

            // Re-validated on the way OUT as well as in. The column is written
            // by us today, but a key that reaches the quote page decides which
            // private object gets streamed to a provider — so the check belongs
            // at the point of use, not only at the point of storage.
            return LeadPhotoNormalizer.KeepIssuedKeys(parsed);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>How many photos this lead carries.</summary>
    public static int Count(string? photoKeysJson) => Keys(photoKeysJson).Count;
}
