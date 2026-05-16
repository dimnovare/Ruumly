using System.Text.Json;

namespace Ruumly.Backend.Models;

public static class BookingExtraSnapshotHelper
{
    /// <summary>
    /// Deserializes the ExtrasJson column into a typed snapshot list.
    /// Handles both the current format (array of BookingExtraSnapshot objects)
    /// and the legacy format (plain string array of keys).
    /// </summary>
    public static List<BookingExtraSnapshot> Deserialize(string extrasJson)
    {
        if (string.IsNullOrWhiteSpace(extrasJson) || extrasJson == "[]")
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<BookingExtraSnapshot>>(extrasJson) ?? [];
        }
        catch (JsonException)
        {
            // Fall back to legacy format: plain string array ["key1","key2"]
            try
            {
                var keys = JsonSerializer.Deserialize<List<string>>(extrasJson) ?? [];
                return keys.Select(k => new BookingExtraSnapshot(k, k, 0, 0, 0)).ToList();
            }
            catch
            {
                return [];
            }
        }
    }
}
