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

    // Estonia bounding box — directory launch is EE-only (Tallinn/Harjumaa first).
    private const double MinLat = 57.5, MaxLat = 59.7;
    private const double MinLng = 21.7, MaxLng = 28.2;

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

            var now  = DateTime.UtcNow;
            var name = row.Name!.Trim();
            var city = row.City!.Trim();

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
                Country         = "EE",
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
                Country     = "EE",
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
        if (row.Lat is not { } lat || lat is < MinLat or > MaxLat)
            return $"lat is required and must be within {MinLat}-{MaxLat}";
        if (row.Lng is not { } lng || lng is < MinLng or > MaxLng)
            return $"lng is required and must be within {MinLng}-{MaxLng}";

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
