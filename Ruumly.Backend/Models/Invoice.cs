using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

public class Invoice
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public decimal Amount { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Pending;
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? PaymentOrderId { get; set; }
    public string? PaymentMethod { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
