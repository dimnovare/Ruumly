using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Services.Interfaces;

public interface IOrderService
{
    Task<PaginatedResult<OrderDto>> GetAllAsync(Guid userId, UserRole role, int page = 1, int limit = 50, Guid? supplierId = null, string? status = null, CancellationToken ct = default);
    Task<Dictionary<string, int>> GetStatusCountsAsync(Guid userId, UserRole role, Guid? supplierId = null, CancellationToken ct = default);
    Task<OrderDto?>       GetByIdAsync(Guid id, Guid callerId, UserRole callerRole, CancellationToken ct = default);
    Task<OrderDto?>       GetByBookingIdAsync(Guid bookingId, Guid callerId, UserRole callerRole, CancellationToken ct = default);
    Task<OrderDto>        ApproveAsync(Guid id, Guid approvedByUserId, CancellationToken ct = default);
    Task<OrderDto>        RejectAsync(Guid id, string reason, Guid rejectedByUserId, CancellationToken ct = default);
    Task<OrderDto>        ConfirmAsync(Guid id, Guid confirmedByUserId, CancellationToken ct = default);
    Task<OrderDto>        UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request, CancellationToken ct = default);
    OrderDto              MapToDto(Models.Order order);
}
