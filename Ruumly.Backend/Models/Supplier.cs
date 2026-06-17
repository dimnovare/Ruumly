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

    // ── API polling ───────────────────────────────────────────────────────────
    /// <summary>Whether the platform should automatically poll this supplier's API.</summary>
    public bool PollingEnabled { get; set; } = false;

    /// <summary>Minutes between polls. Supported values: 15, 30, 60, 360, 1440.</summary>
    public int PollingIntervalMinutes { get; set; } = 60;

    /// <summary>When the next scheduled poll should fire. Null = not yet scheduled.</summary>
    public DateTime? NextPollAt { get; set; }

    /// <summary>When the most recent poll completed (regardless of outcome).</summary>
    public DateTime? LastPolledAt { get; set; }

    /// <summary>Quick status string for the admin list: "ok" | "error" | null.</summary>
    public string? LastPollStatus { get; set; }

    /// <summary>Override URL for availability polling. Falls back to ApiEndpoint when null.</summary>
    public string? PollingEndpoint { get; set; }

    public decimal PartnerDiscountRate { get; set; } = 0;
    public decimal ClientDiscountRate { get; set; } = 0;
    public string? Notes { get; set; }

    // ── Public partner page ────────────────────────────────────────────────
    // These power /partner/{slug} — opt-in per supplier, all nullable, none
    // change behavior for rows that don't set them.

    /// <summary>kebab-case, [a-z0-9-], 2..80 chars, globally unique when set.</summary>
    public string? Slug { get; set; }

    /// <summary>
    /// When false the /partner/:slug page returns 404 even when a slug is set.
    /// Admin controls publish state; partner can edit content but not publish.
    /// </summary>
    public bool IsPartnerPagePublished { get; set; } = false;

    /// <summary>Short pitch shown under the partner name. <= 160 chars.</summary>
    public string? Tagline { get; set; }

    /// <summary>JSON: {"et":"...","en":"...","ru":"..."} — long-form story.</summary>
    public string? LongDescriptionTranslationsJson { get; set; }

    /// <summary>Absolute URL or R2 key.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>Absolute URL or R2 key.</summary>
    public string? HeroImageUrl { get; set; }

    /// <summary>The partner's own site, used as outbound link.</summary>
    public string? WebsiteUrl { get; set; }

    /// <summary>Year founded — for the stats strip on the public page.</summary>
    public int? FoundedYear { get; set; }

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

    // ── Optional commerce capabilities ──────────────────────────────────────
    // Suppliers are free directory partners by default. Admins can enable these
    // capabilities one by one when a partner wants managed bookings, contracts,
    // or payment collection through Ruumly.
    public bool BookingEnabled { get; set; } = false;
    public bool ContractSigningEnabled { get; set; } = false;
    public bool DirectPaymentEnabled { get; set; } = false;
    public bool RuumlyPaymentEnabled { get; set; } = false;

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

    /// <summary>Google Places ID for fetching reviews and ratings via Places API.</summary>
    public string? GooglePlaceId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<Listing> Listings { get; set; } = [];
    public List<Order> Orders { get; set; } = [];
    public IntegrationSettings? IntegrationSettings { get; set; }
}
