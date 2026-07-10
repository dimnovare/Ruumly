namespace Ruumly.Backend.DTOs.Requests;

/// <summary>
/// One option in an offer create/patch payload. Supplier linkage is optional —
/// a free-form option (manual quote, provider not yet in the system) is valid.
/// </summary>
public record OfferOptionInput(
    string Title,
    Guid? SupplierId = null,
    Guid? SupplierLocationId = null,
    decimal? PriceAmount = null,
    string? PriceUnit = null,
    string? Notes = null,
    // Nullable so an EXPLICIT 0 is distinguishable from "not provided"
    // (omitted → the payload index is used).
    int? SortOrder = null
);

/// <summary>POST /api/admin/leads/{id}/offers — create a draft offer.</summary>
public record CreateOfferRequest(
    string? Language = null,
    string? CustomerNote = null,
    List<OfferOptionInput>? Options = null
);

/// <summary>
/// PATCH /api/admin/offers/{id}. Null field = leave unchanged; a non-null
/// Options list REPLACES the whole option set (replace-set semantics, [] clears).
/// </summary>
public record UpdateOfferRequest(
    string? CustomerNote = null,
    string? Language = null,
    string? Status = null,
    List<OfferOptionInput>? Options = null
);

/// <summary>POST /api/admin/leads/{id}/outreach — availability-request batch.</summary>
public record OutreachRequest(List<Guid> SupplierIds);

/// <summary>PATCH /api/admin/outreach/{id} — manual status/note update.</summary>
public record UpdateOutreachRequest(string? Status = null, string? Note = null);

/// <summary>POST /api/offers/{token}/choose — the customer picks an option.</summary>
public record ChooseOptionRequest(Guid OptionId);
