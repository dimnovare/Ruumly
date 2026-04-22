using Ruumly.Backend.DTOs.Responses;

namespace Ruumly.Backend.Services.Interfaces;

public interface IFeaturedPartnersService
{
    Task<List<FeaturedPartnerDto>> GetFeaturedAsync();
}
