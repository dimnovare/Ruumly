using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

public class BookingTimeline
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public string Event { get; set; } = string.Empty;
    public BookingStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
