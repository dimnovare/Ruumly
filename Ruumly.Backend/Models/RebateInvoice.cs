using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

/// <summary>
/// Monthly commission invoice FROM Ruumly TO a rebate-model supplier.
/// Represents what the supplier owes Ruumly for the month.
/// </summary>
public class RebateInvoice
{
    public Guid Id { get; set; }

    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    /// <summary>Billing period — first day of the month, e.g. 2024-05-01</summary>
    public DateTime Period { get; set; }

    /// <summary>Sum of PlatformMargin for all orders in the period</summary>
    public decimal TotalMargin { get; set; }

    /// <summary>Number of orders included</summary>
    public int OrderCount { get; set; }

    public RebateInvoiceStatus Status { get; set; } = RebateInvoiceStatus.Draft;

    /// <summary>When the invoice PDF / email was sent to the supplier</summary>
    public DateTime? SentAt { get; set; }

    /// <summary>When the supplier settled payment</summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>Optional notes or payment reference</summary>
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
