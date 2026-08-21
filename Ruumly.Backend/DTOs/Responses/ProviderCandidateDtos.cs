namespace Ruumly.Backend.DTOs.Responses;

public sealed record ProviderCandidateAnchorDto(double Lat, double Lng);

public sealed record ProviderCandidateLocationDto(
    Guid LocationId,
    string LocationName,
    string City,
    string Address,
    double? Lat,
    double? Lng,
    double? DistanceKm);

public sealed record ProviderCandidateDto(
    Guid SupplierId,
    string SupplierName,
    string? ContactEmail,
    string? ContactPhone,
    IReadOnlyList<string> ServiceTypes,
    Guid? LocationId,
    string? LocationName,
    string? City,
    string? Address,
    double? Lat,
    double? Lng,
    double? DistanceKm,
    bool IsExactCity,
    Guid? ListingId,
    string? ListingTitle,
    decimal? Price,
    string? PriceUnit,
    bool AlreadyContacted,
    DateTime? LastOutreachAt,
    // ContactEmailUnusable: the address hard-bounced or drew a spam complaint
    // (Resend webhook). Auto fan-out skips these, and the workspace badges the
    // row "unreachable" so the gap is visible instead of buried.
    bool ContactEmailUnusable,
    DateTime? ContactEmailBouncedAt,
    // Who this provider will work for. Carried on the candidate for the same
    // reason ContactEmailUnusable is: the send refuses them, so the operator
    // has to be able to SEE why rather than watch a row quietly not get an
    // email. False on either means the fan-out will skip them —
    // "business_only" when they serve companies only, "no_recurring" when the
    // customer asked for an ongoing arrangement and they take one-off work.
    bool ServesConsumers,
    bool ServesRecurring,
    IReadOnlyList<ProviderCandidateLocationDto> OtherLocations);

public sealed record ProviderCandidateResponse(
    IReadOnlyList<ProviderCandidateDto> Items,
    int Total,
    string Scope,
    double RadiusKm,
    ProviderCandidateAnchorDto? Anchor);
