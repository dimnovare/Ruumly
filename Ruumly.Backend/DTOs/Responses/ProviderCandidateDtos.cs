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
    IReadOnlyList<ProviderCandidateLocationDto> OtherLocations);

public sealed record ProviderCandidateResponse(
    IReadOnlyList<ProviderCandidateDto> Items,
    int Total,
    string Scope,
    double RadiusKm,
    ProviderCandidateAnchorDto? Anchor);
