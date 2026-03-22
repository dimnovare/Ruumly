using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

public class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Desc { get; set; } = string.Empty;
    public bool Read { get; set; }
    public string? ActionUrl { get; set; }
    public string? EntityId { get; set; }
    public string? EntityType { get; set; }
    public NotificationChannel Channel { get; set; } = NotificationChannel.InApp;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
