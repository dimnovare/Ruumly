using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;

namespace Ruumly.Backend.Services.Interfaces;

public interface IListingService
{
    Task<PaginatedResult<ListingDto>> SearchAsync(ListingSearchRequest filters);
    Task<ListingDto?>               GetByIdAsync(Guid id);
    Task<List<ListingDto>>          GetFeaturedAsync();
    Task                            InvalidateListingAsync(Guid id);
}
