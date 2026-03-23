using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Services.Interfaces;

public interface IInvoiceService
{
    Task<List<InvoiceDto>>  GetAllAsync(Guid userId, UserRole role);
    Task<InvoiceDto?>       GetByBookingIdAsync(Guid bookingId, Guid userId, UserRole role);
    Task<InvoiceDto>        GenerateAsync(Guid bookingId);
    Task<InvoiceDto>        MarkPaidAsync(Guid id);
}
