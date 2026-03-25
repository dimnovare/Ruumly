using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

public class Supplier
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RegistryCode { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public IntegrationType IntegrationType { get; set; }
    public string? ApiEndpoint { get; set; }
    public string? ApiAuthType { get; set; }
    public string? ApiAuthToken { get; set; }
    public string? RecipientEmail { get; set; }
    public bool IsActive { get; set; } = true;
    public IntegrationHealth IntegrationHealth { get; set; } = IntegrationHealth.Healthy;
    public decimal PartnerDiscountRate { get; set; } = 0;
    public decimal ClientDiscountRate { get; set; } = 0;
    public string? Notes { get; set; }

    /// <summary>
    /// IBAN for payout transfers (e.g. EE382200221011xxx).
    /// Stored and displayed only to admin and the partner themselves.
    /// </summary>
    public string? Iban { get; set; }

    /// <summary>
    /// Bank account holder name (may differ from company name).
    /// </summary>
    public string? BankAccountName { get; set; }

    /// <summary>
    /// Bank name for display purposes (e.g. "LHV", "SEB", "Swedbank").
    /// </summary>
    public string? BankName { get; set; }

    public SupplierTier Tier { get; set; } = SupplierTier.Starter;
    public decimal MonthlyFee { get; set; } = 0m;
    public DateTime? SubscriptionEndsAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<Listing> Listings { get; set; } = [];
    public List<Order> Orders { get; set; } = [];
    public IntegrationSettings? IntegrationSettings { get; set; }
}
