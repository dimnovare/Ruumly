using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

public class OrderRoutingRule
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? SupplierId { get; set; }
    public ListingType? ServiceType { get; set; }
    public string? OrderType { get; set; }   // "standard" | "express" | "business"
    public decimal? PriceThreshold { get; set; }
    public string? CustomerType { get; set; } // "private" | "business"
    public bool RequiresApproval { get; set; }
    public string ApproverRole { get; set; } = string.Empty;
    public PostingMode PostingChannel { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
