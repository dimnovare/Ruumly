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
    IReadOnlyList<ProviderCandidateLocationDto> OtherLocations);

public sealed record ProviderCandidateResponse(
    IReadOnlyList<ProviderCandidateDto> Items,
    int Total,
    string Scope,
    double RadiusKm,
    ProviderCandidateAnchorDto? Anchor);
