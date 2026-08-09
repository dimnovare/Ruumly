namespace Ruumly.Backend.DTOs.Requests;

/// <summary>
/// One-off supplier introduction campaign. Every field is optional and the
/// safe value is the default: an empty body <c>{}</c> is a DRY RUN over every
/// eligible supplier, which sends nothing.
/// </summary>
/// <param name="DryRun">
/// DEFAULTS TO TRUE, including when the property is omitted or explicitly
/// null. Sending is opt-in per request — the founder must pass
/// <c>"dryRun": false</c> deliberately. Do not change this default.
/// </param>
/// <param name="Limit">Cap on how many suppliers are targeted (1..1000). Null = no cap.</param>
/// <param name="Country">Two-letter filter (EE/LV/LT/...), case-insensitive. Null = all.</param>
/// <param name="SupplierIds">
/// Explicit recipients — overrides <paramref name="Country"/>. Intended for the
/// first live batch: send to two or three of your own addresses before the rest.
/// </param>
/// <param name="IncludeNonDirectory">
/// The copy says "we added you from publicly available information", which is
/// false for a partner who signed up themselves, so non-directory suppliers are
/// skipped by default (reason <c>not_directory</c>) rather than told something
/// untrue. Set true to include them anyway.
/// </param>
public record SupplierIntroCampaignRequest(
    bool? DryRun = null,
    int? Limit = null,
    string? Country = null,
    IReadOnlyList<Guid>? SupplierIds = null,
    bool IncludeNonDirectory = false)
{
    /// <summary>True unless the caller explicitly asked for a live send.</summary>
    public bool IsDryRun => DryRun ?? true;
}
