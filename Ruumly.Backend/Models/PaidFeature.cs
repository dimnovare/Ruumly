using System.ComponentModel.DataAnnotations;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

public class PaidFeature
{
    public Guid Id { get; set; }

    [MaxLength(80)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    public PaidFeatureCategory Category { get; set; } = PaidFeatureCategory.Visibility;
    public PaidFeatureScope Scope { get; set; } = PaidFeatureScope.Supplier;

    public decimal PriceAmount { get; set; } = 0m;

    [MaxLength(3)]
    public string PriceCurrency { get; set; } = "EUR";

    [MaxLength(40)]
    public string BillingInterval { get; set; } = "manual";

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
