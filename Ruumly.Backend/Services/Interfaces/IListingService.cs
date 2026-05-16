using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;

namespace Ruumly.Backend.Services.Interfaces;

public interface IListingService
{
    Task<PaginatedResult<ListingDto>> SearchAsync(ListingSearchRequest filters, string? language = null, CancellationToken ct = default);
    Task<ListingDto?>               GetByIdAsync(Guid id, string? language = null);
    Task<List<ListingDto>>          GetFeaturedAsync(string? language = null);
    Task                            InvalidateListingAsync(Guid id);
}
