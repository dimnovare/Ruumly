using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

public class Order
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    // Denormalised snapshot of the booking/listing at order creation time
    public Guid ListingId { get; set; }
    public string ListingTitle { get; set; } = string.Empty;
    public ListingType ListingType { get; set; }
    public string City { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Duration { get; set; } = string.Empty;

    /// <summary>
    /// JSON array of { key, label, supplierPrice, customerPrice } objects.
    /// Snapshot copied from Booking at order creation time.
    /// </summary>
    public string ExtrasJson { get; set; } = "[]";

    [NotMapped]
    public List<BookingExtraSnapshot> ExtrasSnapshot
    {
        get => JsonSerializer.Deserialize<List<BookingExtraSnapshot>>(ExtrasJson) ?? [];
        set => ExtrasJson = JsonSerializer.Serialize(value);
    }

    [NotMapped]
    public List<string> ExtrasKeys => ExtrasSnapshot.Select(e => e.Key).ToList();

    public IntegrationType IntegrationType { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public decimal BasePrice { get; set; }
    public decimal PlatformPrice { get; set; }
    public decimal SupplierPrice { get; set; }
    public decimal ExtrasTotal { get; set; }
    public decimal Total { get; set; }
    public decimal Margin { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Created;
    public ApprovalMode? ApprovalMode { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public PostingMode? PostingChannel { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<FulfillmentEvent> FulfillmentEvents { get; set; } = [];
    public List<OrderTimeline> Timeline { get; set; } = [];
}
