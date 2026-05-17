using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Services.Interfaces;

public interface IInvoiceService
{
    Task<PaginatedResult<InvoiceDto>> GetAllAsync(Guid userId, UserRole role, int page = 1, int limit = 100);
    Task<InvoiceDto?>       GetByBookingIdAsync(Guid bookingId, Guid userId, UserRole role);
    Task<InvoiceDto>        GenerateAsync(Guid bookingId);
    Task<InvoiceDto>        MarkPaidAsync(Guid id);
}
