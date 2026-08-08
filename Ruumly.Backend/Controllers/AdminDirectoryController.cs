using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Constants;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Controllers;

/// <summary>
/// Admin bulk import for the provider DIRECTORY: unclaimed free profiles that get
/// a map pin and a public partner page (/partner/{slug}) without any commerce.
/// Each row creates a Supplier (IsDirectoryListing = true, published) plus exactly
/// one geolocated, non-synthetic SupplierLocation with zero units.
/// </summary>
[Route("api/admin")]
public partial class AdminDirectoryController(RuumlyDbContext db) : AdminBaseController(db)
{
    private const int MaxRows = 500;

    /// <summary>
    /// Country used when a row omits <c>country</c>. This is DELIBERATE backward
    /// compatibility, not an oversight: the endpoint launched EE-only and every row
    /// already imported into production predates the field, so a missing country has
    /// to keep meaning Estonia. Do not "fix" this to null/required without migrating
    /// the existing import scripts — and note the stakes: Supplier.Country picks the
    /// language of the provider outreach email (Helpers/ProviderOutreachComposer),
    /// so a wrong country cold-emails a Latvian provider in Estonian.
    /// </summary>
    private const string DefaultCountry = "EE";

    private readonly record struct GeoBox(double MinLat, double MaxLat, double MinLng, double MaxLng);

    /// <summary>
    /// Bounding box per supported market. Opening a new country is ONE line here —
    /// validation, the allowed-values error text and the stored Country all read
    /// from this map.
    /// </summary>
    private static readonly Dictionary<string, GeoBox> CountryBounds = new(StringComparer.Ordinal)
    {
        ["EE"] = new(57.5, 59.7, 21.7, 28.2),
        ["LV"] = new(55.6, 58.1, 20.9, 28.3),
        ["LT"] = new(53.8, 56.5, 20.9, 26.9),
    };

    private static readonly string[] ReservedSlugs =
        ["dashboard", "onboarding", "new", "edit", "admin", "api"];

    [GeneratedRegex("^[a-z0-9-]{2,80}$")]
    private static partial Regex SlugShape();

    /// <summary>
    /// Bulk-creates unclaimed directory suppliers. Idempotent by slug: a row whose
    /// slug already exists is skipped (reported, never modified). Row-level
    /// validation errors never fail the batch. Cap 500 rows per request.
    /// </summary>
    [HttpPost("suppliers/bulk")]
    public async Task<IActionResult> BulkCreateDirectorySuppliers(
        [FromBody] List<DirectorySupplierRow> rows)
    {
        if (rows is null || rows.Count == 0)
            return BadRequest(Error("Request body must be a non-empty JSON array."));
        if (rows.Count > MaxRows)
            return BadRequest(Error($"Maximum {MaxRows} rows per request."));

        var created = new List<string>();
        var skipped = new List<string>();
        var errors  = new List<object>();
        // Slugs created earlier in this batch — an in-batch duplicate is a skip,
        // exactly like a slug that already existed before the call.
        var batchSlugs = new HashSet<string>();

        for (var i = 0; i < rows.Count; i++)
        {
            var row    = rows[i];
            var slug   = row.Slug?.Trim().ToLowerInvariant() ?? "";
            var reason = ValidateRow(row, slug);
            if (reason is not null)
            {
                errors.Add(new { slug, reason = $"Row {i + 1}: {reason}" });
                continue;
            }

            if (batchSlugs.Contains(slug) || await Db.Suppliers.AnyAsync(s => s.Slug == slug))
            {
                skipped.Add(slug);
                continue;
            }

            var now     = DateTime.UtcNow;
            var name    = row.Name!.Trim();
            var city    = row.City!.Trim();
            // Validated above: guaranteed to be a supported, upper-cased code.
            var country = NormalizeCountry(row.Country);

            var supplier = new Supplier
            {
                Id              = Guid.NewGuid(),
                Name            = name,
                Slug            = slug,
                RegistryCode    = NullIfBlank(row.RegistryCode),
                ContactName     = name,
                ContactEmail    = row.ContactEmail?.Trim() ?? "",
                ContactPhone    = row.ContactPhone?.Trim() ?? "",
                WebsiteUrl      = NullIfBlank(row.WebsiteUrl),
                Tagline         = Clamp(NullIfBlank(row.Tagline), 160),
                LongDescriptionTranslationsJson = BuildDescriptionsJson(row),
                LogoUrl         = NullIfBlank(row.LogoUrl),
                IntegrationType = IntegrationType.Manual,
                // Also picks the provider outreach email language — see DefaultCountry.
                Country         = country,
                IsActive        = true,
                IsPartnerPagePublished = true,
                IsDirectoryListing     = true,
                ServiceTypesJson       = JsonSerializer.Serialize(NormalizeServiceTypes(row.ServiceTypes!)),
                // Commerce toggles stay false (class defaults): no bookings,
                // contracts, or payments for unclaimed profiles.
                CreatedAt       = now,
                UpdatedAt       = now,
            };

            var location = new SupplierLocation
            {
                Id          = Guid.NewGuid(),
                SupplierId  = supplier.Id,
                Name        = name,
                Address     = NullIfBlank(row.Address) ?? city,
                City        = city,
                Country     = country,
                Lat         = row.Lat!.Value,
                Lng         = row.Lng!.Value,
                IsActive    = true,
                IsSynthetic = false,
                Description = string.Empty,
                CreatedAt   = now,
                UpdatedAt   = now,
            };

            try
            {
                Db.Suppliers.Add(supplier);
                Db.SupplierLocations.Add(location);
                await Db.SaveChangesAsync();
                created.Add(slug);
                batchSlugs.Add(slug);
            }
            catch (Exception)
            {
                // e.g. duplicate RegistryCode unique-index violation — report the
                // row, drop its pending entities, and keep going.
                Db.ChangeTracker.Clear();
                errors.Add(new { slug, reason = $"Row {i + 1}: database error — supplier not saved" });
            }
        }

        Audit("directory.bulk_import", User.GetUserEmail(), "Directory suppliers",
            $"created={created.Count} skipped={skipped.Count} errors={errors.Count} of {rows.Count} row(s)");
        await Db.SaveChangesAsync();

        return Ok(new { created, skipped, errors });
    }

    /// <summary>Returns a human-readable reason when the row is invalid, else null.</summary>
    private static string? ValidateRow(DirectorySupplierRow row, string slug)
    {
        if (string.IsNullOrWhiteSpace(row.Name))
            return "name is required";
        if (slug.Length == 0)
            return "slug is required";
        if (!SlugShape().IsMatch(slug))
            return "slug must be 2-80 lowercase letters, digits, or hyphens";
        if (ReservedSlugs.Contains(slug))
            return $"'{slug}' is a reserved slug";
        if (string.IsNullOrWhiteSpace(row.City))
            return "city is required";

        // Coordinates are checked against the box of the row's OWN country, so a
        // Riga pin imported as "EE" is rejected instead of silently landing in the
        // Estonian directory (and later being cold-emailed in Estonian).
        var country = NormalizeCountry(row.Country);
        if (!CountryBounds.TryGetValue(country, out var box))
            return $"unknown country '{country}' (allowed: {string.Join("|", CountryBounds.Keys)})";
        if (row.Lat is not { } lat)
            return "lat is required";
        if (lat < box.MinLat || lat > box.MaxLat)
            return FormattableString.Invariant(
                $"coordinates outside {country} bounds: lat must be within {box.MinLat}-{box.MaxLat}");
        if (row.Lng is not { } lng)
            return "lng is required";
        if (lng < box.MinLng || lng > box.MaxLng)
            return FormattableString.Invariant(
                $"coordinates outside {country} bounds: lng must be within {box.MinLng}-{box.MaxLng}");

        // Imported rows come from scraped third-party data and render on public
        // pages (map popups, partner pages) — refuse anything markup-shaped and
        // require sane URL schemes/emails so poisoned data never persists.
        foreach (var (label, value) in new[]
                 { ("name", row.Name), ("city", row.City), ("address", row.Address), ("tagline", row.Tagline) })
        {
            if (value is not null && (value.Contains('<') || value.Contains('>')))
                return $"{label} must not contain '<' or '>'";
        }
        if (!IsNullOrHttpUrl(row.WebsiteUrl))
            return "websiteUrl must start with http:// or https://";
        if (!IsNullOrHttpUrl(row.LogoUrl))
            return "logoUrl must start with http:// or https://";
        if (!string.IsNullOrWhiteSpace(row.ContactEmail) && !EmailValidation.IsValid(row.ContactEmail))
            return "contactEmail is not a valid email address";

        var serviceTypes = NormalizeServiceTypes(row.ServiceTypes);
        if (serviceTypes.Count == 0)
            return "serviceTypes must be a non-empty array";
        var invalid = serviceTypes.Where(t => !ServiceCategories.BySlug.ContainsKey(t)).ToList();
        if (invalid.Count > 0)
            return $"unknown serviceTypes: {string.Join(", ", invalid)} " +
                   $"(allowed: {string.Join("|", ServiceCategories.BySlug.Keys)})";

        return null;
    }

    /// <summary>
    /// Trims and upper-cases the row's country ("lv" → "LV"); a missing/blank value
    /// falls back to <see cref="DefaultCountry"/>. Whether the result is a supported
    /// market is decided by <see cref="CountryBounds"/> in ValidateRow.
    /// </summary>
    private static string NormalizeCountry(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? DefaultCountry : raw.Trim().ToUpperInvariant();

    private static bool IsNullOrHttpUrl(string? url) =>
        string.IsNullOrWhiteSpace(url)
        || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static List<string> NormalizeServiceTypes(List<string>? raw) =>
        (raw ?? [])
            .Select(t => t?.Trim().ToLowerInvariant() ?? "")
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();

    /// <summary>
    /// {"et","en","ru"} JSON from the per-language descriptions, each language
    /// falling back to whichever one was provided; null when none given.
    /// </summary>
    private static string? BuildDescriptionsJson(DirectorySupplierRow row)
    {
        var et = NullIfBlank(row.DescriptionEt);
        var en = NullIfBlank(row.DescriptionEn);
        var ru = NullIfBlank(row.DescriptionRu);
        if (et is null && en is null && ru is null) return null;

        return JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["et"] = (et ?? en ?? ru)!,
            ["en"] = (en ?? et ?? ru)!,
            ["ru"] = (ru ?? en ?? et)!,
        });
    }

    private static string? NullIfBlank(string? s)
    {
        var trimmed = s?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string? Clamp(string? s, int max) =>
        s is { Length: > 0 } && s.Length > max ? s[..max] : s;
}
