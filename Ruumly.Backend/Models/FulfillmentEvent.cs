using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

public class FulfillmentEvent
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public FulfillmentStatus Status { get; set; }
    public string Actor { get; set; } = string.Empty;
    public UserRole ActorRole { get; set; }
    public PostingMode? Channel { get; set; }
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
