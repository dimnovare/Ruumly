using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Services.Interfaces;

public interface IOrderService
{
    Task<PaginatedResult<OrderDto>> GetAllAsync(Guid userId, UserRole role, int page = 1, int limit = 50);
    Task<OrderDto?>       GetByIdAsync(Guid id);
    Task<OrderDto?>       GetByBookingIdAsync(Guid bookingId);
    Task<OrderDto>        ApproveAsync(Guid id, Guid approvedByUserId);
    Task<OrderDto>        RejectAsync(Guid id, string reason, Guid rejectedByUserId);
    Task<OrderDto>        ConfirmAsync(Guid id, Guid confirmedByUserId);
    Task<OrderDto>        UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request);
}
