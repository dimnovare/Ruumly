using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Services.Interfaces;

public interface IMessageService
{
    Task<List<MessageDto>> GetByBookingIdAsync(Guid bookingId, Guid userId, UserRole role);
    Task<MessageDto>       SendAsync(Guid bookingId, SendMessageRequest request, Guid userId, UserRole role);
    Task                   MarkReadAsync(Guid bookingId, Guid userId, UserRole role);
}
