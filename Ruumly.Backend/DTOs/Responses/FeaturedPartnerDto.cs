namespace Ruumly.Backend.DTOs.Responses;

public record FeaturedPartnerDto(
    Guid    Id,
    string? Slug,
    string  Name,
    string? Tagline,
    string? LogoUrl,
    string  Country,
    decimal Rating,
    int     ReviewCount,
    string  Tier,
    bool    IsVerified,
    int     LocationCount,
    int     ListingCount);
