using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Services.Interfaces;

public interface IBookingService
{
    Task<PaginatedResult<BookingDto>> GetAllAsync(Guid userId, UserRole role, int page = 1, int limit = 50);
    Task<BookingDto?>      GetByIdAsync(Guid id, Guid userId, UserRole role);
    Task<BookingDto>       CreateAsync(CreateBookingRequest request, Guid userId);
    Task<BookingDto>       CancelAsync(Guid id, Guid userId, UserRole role);
    IReadOnlyDictionary<string, decimal> GetExtrasPrices();
}
