namespace Ruumly.Backend.DTOs.Requests;

/// <summary>
/// One row of the admin directory bulk import (POST /api/admin/suppliers/bulk).
/// Everything is nullable so a bad/missing value fails only that row —
/// the controller validates per row and never aborts the batch.
/// </summary>
/// <param name="Country">
/// Two-letter market code — "EE", "LV" or "LT", case-insensitive, normalized to
/// upper case. Omitted/blank means "EE" (see AdminDirectoryController.DefaultCountry:
/// the endpoint launched EE-only and the rows already in production carry no
/// country). An unsupported value fails only that row, like any other bad field.
/// It also decides the language of the provider outreach email
/// (Helpers/ProviderOutreachComposer switches on Supplier.Country), so importing
/// a Latvian provider as "EE" would cold-email them in Estonian.
/// </param>
public record DirectorySupplierRow(
    string?       Name,
    string?       Slug,
    string?       City,
    double?       Lat,
    double?       Lng,
    List<string>? ServiceTypes,
    string?       Address       = null,
    string?       Country       = null,
    string?       WebsiteUrl    = null,
    string?       ContactEmail  = null,
    string?       ContactPhone  = null,
    string?       Tagline       = null,
    string?       DescriptionEt = null,
    string?       DescriptionEn = null,
    string?       DescriptionRu = null,
    string?       LogoUrl       = null,
    string?       RegistryCode  = null);
