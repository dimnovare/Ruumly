using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Constants;

/// <summary>
/// Canonical service-category slugs: every concrete <see cref="DemandLeadCategory"/>
/// name lower-cased (the Any wildcard excluded). One source of truth shared by the
/// concierge intake (POST /api/leads/request) and the provider directory
/// (Supplier.ServiceTypesJson), so a category added to the enum is automatically
/// accepted everywhere.
/// </summary>
public static class ServiceCategories
{
    /// <summary>slug → enum, e.g. "vanrental" → DemandLeadCategory.VanRental.</summary>
    public static readonly IReadOnlyDictionary<string, DemandLeadCategory> BySlug =
        Enum.GetValues<DemandLeadCategory>()
            .Where(c => c != DemandLeadCategory.Any)
            .ToDictionary(c => c.ToString().ToLowerInvariant(), c => c);

    /// <summary>Slug for a concrete category ("cleaning"); Any → "any".</summary>
    public static string SlugFor(DemandLeadCategory category) =>
        category.ToString().ToLowerInvariant();

    /// <summary>
    /// Tolerant parse of a Supplier.ServiceTypesJson value (JSON array of slugs).
    /// Null, empty, or malformed input yields an empty list — directory rows must
    /// never fail a request because one imported record carries bad JSON.
    /// </summary>
    public static List<string> ParseServiceTypes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }
}
