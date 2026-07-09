namespace Ruumly.Backend.DTOs.Requests;

/// <summary>
/// One row of the admin directory bulk import (POST /api/admin/suppliers/bulk).
/// Everything is nullable so a bad/missing value fails only that row —
/// the controller validates per row and never aborts the batch.
/// </summary>
public record DirectorySupplierRow(
    string?       Name,
    string?       Slug,
    string?       City,
    double?       Lat,
    double?       Lng,
    List<string>? ServiceTypes,
    string?       Address       = null,
    string?       WebsiteUrl    = null,
    string?       ContactEmail  = null,
    string?       ContactPhone  = null,
    string?       Tagline       = null,
    string?       DescriptionEt = null,
    string?       DescriptionEn = null,
    string?       DescriptionRu = null,
    string?       LogoUrl       = null,
    string?       RegistryCode  = null);
