using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

public class Supplier
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? RegistryCode { get; set; }
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

    public decimal Rating { get; set; }
    public int ReviewCount { get; set; }

    public string Country { get; set; } = "EE";

    public BillingModel BillingModel { get; set; } = BillingModel.Marketplace;

    // TODO(billing): add BillingMode { B2C, B2B } enum.
    // B2C suppliers (default): listings show VAT-inclusive prices to all visitors.
    // B2B suppliers: listings show VAT-exclusive prices when the customer is a
    // verified business buyer (registered with VAT number). Out of scope for v1.

    public SupplierTier Tier { get; set; } = SupplierTier.Starter;
    public decimal MonthlyFee { get; set; } = 0m;
    public DateTime? SubscriptionEndsAt { get; set; }

    public bool FoundingPartner { get; set; }

    // Onboarding window — 0% commission, 0€ subscription for first 90 days after supplier is activated.
    // Set when admin activates (IsActive flips to true); starts the clock.
    public DateTime? OnboardingStartedAt { get; set; }
    public bool IsInOnboarding => OnboardingStartedAt.HasValue
        && OnboardingStartedAt.Value.AddDays(90) > DateTime.UtcNow;

    // Priority support flag — Business tier + manual admin grant.
    public PriorityLevel PriorityLevel { get; set; } = PriorityLevel.Standard;

    // Admin-granted verified badge — Business tier only, KYC-confirmed.
    public bool IsVerified { get; set; } = false;
    public DateTime? VerifiedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<Listing> Listings { get; set; } = [];
    public List<Order> Orders { get; set; } = [];
    public IntegrationSettings? IntegrationSettings { get; set; }
}
