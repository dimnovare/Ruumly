using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Services.Interfaces;

public interface IBookingService
{
    Task<List<BookingDto>> GetAllAsync(Guid userId, UserRole role);
    Task<BookingDto?>      GetByIdAsync(Guid id, Guid userId, UserRole role);
    Task<BookingDto>       CreateAsync(CreateBookingRequest request, Guid userId);
}
