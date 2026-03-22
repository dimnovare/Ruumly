using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

public class OrderTimeline
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public string Event { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
