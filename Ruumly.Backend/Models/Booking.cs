using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

public class Booking
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid ListingId { get; set; }
    public Listing Listing { get; set; } = null!;
    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Duration { get; set; } = string.Empty;

    public string ExtrasJson { get; set; } = "[]";

    [NotMapped]
    public List<string> Extras
    {
        get => JsonSerializer.Deserialize<List<string>>(ExtrasJson) ?? [];
        set => ExtrasJson = JsonSerializer.Serialize(value);
    }

    public decimal BasePrice { get; set; }
    public decimal PlatformPrice { get; set; }
    public decimal ExtrasTotal { get; set; }
    public decimal Total { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Order? Order { get; set; }
    public Invoice? Invoice { get; set; }
    public List<Message> Messages { get; set; } = [];
    public List<BookingTimeline> Timeline { get; set; } = [];
}
