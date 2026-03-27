using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

public class PayoutEntry
{
    public Guid Id { get; set; }

    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    /// <summary>Amount owed to the supplier for this order</summary>
    public decimal SupplierAmount { get; set; }

    /// <summary>Ruumly margin on this order</summary>
    public decimal PlatformMargin { get; set; }

    /// <summary>Pending = not yet paid, Paid = transferred, Disputed = under review</summary>
    public PayoutStatus Status { get; set; } = PayoutStatus.Pending;

    /// <summary>When admin marked this as paid (bank transfer done)</summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>Bank reference or note</summary>
    public string? PaymentReference { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
