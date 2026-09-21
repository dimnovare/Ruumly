using System.Text.Json.Serialization;

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
/// <remarks>
/// UNMAPPED MEMBERS ARE REFUSED, and this is a deliberate exception to the
/// "a bad row never fails the batch" rule above.
///
/// System.Text.Json's default is to SKIP a property it does not recognise. The
/// import therefore accepted a payload carrying fields this record has no place
/// for - priceFrom, priceUnit, priceNote among them - created every row without
/// them, and answered 200 "created". The caller was told the import had worked.
///
/// An unknown field is a SCHEMA error, not a DATA error: the payload was built
/// against a different contract, so every row in it is suspect, not one. Failing
/// the whole batch with a 400 is the honest answer, and it is strictly better
/// than silently discarding a column somebody meant to import.
///
/// Scoped to this record on purpose. Setting UnmappedMemberHandling.Disallow
/// globally in AddJsonOptions would apply it to the Montonio, Dokobit and Resend
/// callbacks too, and those providers add fields to their payloads without
/// notice - turning a third-party schema change into a 400 on the endpoints that
/// handle money and signatures.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
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
