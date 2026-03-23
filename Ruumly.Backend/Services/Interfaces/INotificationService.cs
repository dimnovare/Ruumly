using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Services.Interfaces;

public interface INotificationService
{
    Task<List<NotificationDto>> GetAllAsync(Guid userId);
    Task MarkReadAsync(Guid id, Guid userId);
    Task MarkAllReadAsync(Guid userId);

    /// <summary>Internal helper — creates and persists a single notification.</summary>
    Task CreateAsync(
        Guid             userId,
        NotificationType type,
        string           title,
        string           desc,
        string?          actionUrl  = null,
        string?          entityId   = null,
        string?          entityType = null);
}
