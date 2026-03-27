using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

public record BookingExtraSnapshot(
    string Key, string Label, decimal SupplierPrice, decimal CustomerPrice);

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

    /// <summary>
    /// JSON array of { key, label, supplierPrice, customerPrice } objects.
    /// Snapshot at booking time — immune to later price changes.
    /// </summary>
    public string ExtrasJson { get; set; } = "[]";

    [NotMapped]
    public List<BookingExtraSnapshot> ExtrasSnapshot
    {
        get => JsonSerializer.Deserialize<List<BookingExtraSnapshot>>(ExtrasJson) ?? [];
        set => ExtrasJson = JsonSerializer.Serialize(value);
    }

    // Keep backward compat: plain string list for order dispatch
    [NotMapped]
    public List<string> ExtrasKeys => ExtrasSnapshot.Select(e => e.Key).ToList();

    public decimal BasePrice { get; set; }
    public decimal PlatformPrice { get; set; }
    public decimal ExtrasTotal { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Total { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Order? Order { get; set; }
    public Invoice? Invoice { get; set; }
    public List<Message> Messages { get; set; } = [];
    public List<BookingTimeline> Timeline { get; set; } = [];
}
