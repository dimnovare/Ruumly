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
    int? SortOrder = null,
    // Which option this input IS — the id the client read back with the offer.
    // PATCH is replace-set, so without it every save deleted the row and minted
    // a new one, throwing away the provenance stored on it (see
    // OfferOption.CreatedFromOutreachId). Omitted, unknown, or belonging to
    // another offer all mean the same, harmless thing: a new option.
    Guid? Id = null
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
/// Replace-set is about membership, not identity: an option the payload still
/// carries by <see cref="OfferOptionInput.Id"/> keeps its row.
///
/// <paramref name="Version"/> is the optimistic-concurrency guard: echo back the
/// version read with the offer and a stale write 409s instead of silently
/// deleting an option seeded since (e.g. by a provider quote). Omitting it skips
/// the check — so clients that don't send it keep the old clobbering behaviour.
/// </summary>
public record UpdateOfferRequest(
    string? CustomerNote = null,
    string? Language = null,
    string? Status = null,
    List<OfferOptionInput>? Options = null,
    int? Version = null
);

/// <summary>POST /api/admin/leads/{id}/outreach/preview — inspect an outreach batch.</summary>
public record OutreachPreviewRequest(List<Guid> SupplierIds);

/// <summary>POST /api/admin/leads/{id}/outreach — availability-request batch.</summary>
public record OutreachRequest(List<Guid> SupplierIds, bool Resend = false);

/// <summary>PATCH /api/admin/outreach/{id} — manual status/note update.</summary>
public record UpdateOutreachRequest(string? Status = null, string? Note = null);

/// <summary>POST /api/offers/{token}/choose — the customer picks an option.</summary>
public record ChooseOptionRequest(Guid OptionId);

/// <summary>
/// POST /api/quote/{token} — a provider submits their price for the outreach
/// lead. PriceAmount is required (≥ 0); the optional strings are trimmed,
/// clamped and reject angle brackets before storage.
/// </summary>
public record SubmitQuoteRequest(
    decimal PriceAmount,
    string? PriceUnit = null,
    string? Availability = null,
    string? Note = null);
