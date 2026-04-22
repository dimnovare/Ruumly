namespace Ruumly.Backend.DTOs.Responses;

public record FeaturedPartnerDto(
    Guid    Id,
    string  Name,
    string  Country,
    decimal Rating,
    int     ReviewCount,
    string  Tier,
    bool    IsVerified,
    int     ListingCount);
