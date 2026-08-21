using System.ComponentModel.DataAnnotations;
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

    // ── Directory (unclaimed free profiles) ─────────────────────────────────
    /// <summary>
    /// True for suppliers imported as unclaimed directory profiles: they get a
    /// map pin and a public partner page but no logged-in owner and no commerce.
    /// </summary>
    public bool IsDirectoryListing { get; set; } = false;

    /// <summary>
    /// JSON array of service category slugs this provider covers, e.g.
    /// ["moving","cleaning"]. Slugs match DemandLeadCategory names lower-cased.
    /// </summary>
    [MaxLength(400)]
    public string? ServiceTypesJson { get; set; }

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

    /// <summary>
    /// When the one-off supplier INTRODUCTION email was sent to this row
    /// ("who Ruumly is, you are listed, requests may arrive"). Non-null means
    /// NEVER send it again.
    ///
    /// This column is the ONLY idempotency guard for a cold campaign to ~435
    /// businesses that have never heard of us — a duplicate send is
    /// unrecoverable reputational damage, so the state is persisted per
    /// supplier and stamped inside the same transaction that claims the row.
    /// Never replace it with an in-memory or per-request guard.
    /// </summary>
    public DateTime? IntroEmailSentAt { get; set; }

    /// <summary>
    /// The headline "from" price this provider quotes, and the unit it is quoted
    /// in ("/h", "/24h", "/m³ kuus"). Deliberately a single figure on the
    /// SUPPLIER rather than a catalogue of <see cref="Listing"/> rows.
    ///
    /// Listing.PriceFrom is the real per-unit price, but reaching it needs a user
    /// account, a provider dashboard and a unit catalogue — and the introduction
    /// campaign sells "no account to create" as a feature, which it should. So a
    /// business that claimed its profile had no way at all to tell us a price,
    /// while that same letter asked them for one. This closes that.
    ///
    /// It is what the concierge actually needs: enough to put a number in front
    /// of a customer within minutes instead of relaying a question and losing
    /// them. A provider who later wants real inventory still gets Listings.
    /// </summary>
    public decimal? PriceFrom { get; set; }

    /// <summary>Unit for <see cref="PriceFrom"/>, in the provider's own words.</summary>
    public string? PriceUnit { get; set; }

    /// <summary>
    /// Everything a single figure cannot carry: what the price includes, VAT
    /// treatment, deposits, surcharges ("piano +80 €"), minimum hours. Free text
    /// because a mover, a trailer yard and a storage site price nothing alike,
    /// and a schema that fits all three fits none of them well.
    /// </summary>
    public string? PriceNote { get; set; }

    /// <summary>
    /// When this business asked us to stop contacting it — the REMOVE reply the
    /// introduction email promises to honour, or any equivalent request by phone
    /// or in person. Non-null means SEND NOTHING, ever: no intro campaign, no
    /// concierge fan-out, and never a candidate for a customer request.
    ///
    /// Separate from <see cref="IsActive"/> on purpose. Deactivating hides a row
    /// from the directory, but it is a routine operational state that a re-import,
    /// a bulk fix or a well-meaning cleanup can flip back on — and the business
    /// would silently start receiving mail again. This flag is the durable record
    /// of a decision the business made, and nothing but an explicit
    /// re-subscription clears it.
    ///
    /// First exercised 2026-08-13, within hours of the first campaign: a Kaunas
    /// cleaning company replied REMOVE. It was honoured by hand because this
    /// column did not exist yet.
    /// </summary>
    public DateTime? MarketingOptOutAt { get; set; }

    /// <summary>
    /// Free text for how the opt-out arrived ("REMOVE reply 2026-08-13",
    /// "asked on the phone"), so a later reader can tell a real request from a
    /// mistaken bulk edit. Never shown publicly.
    /// </summary>
    public string? MarketingOptOutReason { get; set; }

    /// <summary>
    /// When someone proved control of <see cref="ContactEmail"/> through the
    /// public claim flow (/{lang}/claim/{slug}) and took ownership of this
    /// directory profile. Null = still unclaimed.
    ///
    /// Stamped ONCE, at the first successful magic-link redemption, so it stays
    /// an honest "when did this business first respond" figure for the campaign
    /// uptake metric. A returning owner re-verifying does not reset it.
    /// </summary>
    public DateTime? ClaimedAt { get; set; }

    /// <summary>
    /// The verified address that most recently claimed this profile — always an
    /// address that received a magic link, never one a visitor merely typed.
    /// </summary>
    [MaxLength(200)]
    public string? ClaimedByEmail { get; set; }

    // ── Email deliverability (2026-08, Resend webhook) ──────────────────────
    // Before this, a bounce died inside Resend and the outreach row still read
    // "sent" forever — a dead address was indistinguishable from a provider who
    // read the mail and ignored it. The Resend webhook now writes the outcome
    // back here, against the supplier whose address bounced.

    /// <summary>
    /// True when the last delivery attempt to <see cref="ContactEmail"/> HARD
    /// bounced or drew a spam complaint: the address is dead, not merely
    /// unanswered. Auto fan-out and the admin outreach batch both skip these
    /// rows (reason "email_bounced") and pick the next candidate instead.
    /// Cleared automatically when an admin saves a different ContactEmail —
    /// a new address deserves a fresh attempt.
    /// </summary>
    public bool ContactEmailUnusable { get; set; } = false;

    // ── Who this provider will actually work for (2026-08-21) ────────────────
    //
    // A service slug says WHAT a company does, never WHO for. "cleaning" turned
    // out to cover at least three businesses that do not substitute for each
    // other — domestic recurring upkeep, domestic one-off specialist work
    // (suurpuhastus, windows, floor oiling), and commercial/B2B — and
    // ProviderCandidateFinder could not tell them apart, so one Viimsi
    // customer's weekly home clean was sent to seventeen providers of which
    // four replied and three said, in different words, "not for someone like
    // you".
    //
    // Both DEFAULT TO TRUE, so nothing changes until somebody records an actual
    // refusal. A default of false would silently empty the fan-out for 1,187
    // rows nobody has classified.
    //
    // These are NOT enforced in the candidate finder. They follow
    // ContactEmailUnusable rather than MarketingOptOutAt: the provider stays
    // visible to the admin with the reason attached, and the send is what
    // refuses. A supplier that vanishes from the list teaches an operator
    // nothing, and this codebase has already been bitten by "safe" behaviour
    // that turns a real loss into silence.

    /// <summary>
    /// False when the provider serves BUSINESS customers only. Lux Puhastus:
    /// "Pakume teenust vaid äriklientidele." Every Ruumly request today comes
    /// from a private person, so a false here means no request we currently
    /// generate is worth their inbox.
    /// </summary>
    public bool ServesConsumers { get; set; } = true;

    /// <summary>
    /// False when the provider takes one-off jobs but not a RECURRING
    /// arrangement. Kendra: "regulaarset hoolduskoristust eraklientidele ei
    /// paku… teostame ainult ühekordseid eritöid."
    ///
    /// Deliberately not the same thing as <see cref="ServesConsumers"/>: such a
    /// provider is a perfectly good match for a move-out clean and a bad one
    /// for a weekly contract, so this gate only bites when the LEAD is
    /// recurring (see LeadScope.WantsRecurringService).
    /// </summary>
    public bool ServesRecurring { get; set; } = true;

    /// <summary>When the most recent bounce/complaint for this address arrived.</summary>
    public DateTime? ContactEmailBouncedAt { get; set; }

    /// <summary>"hard" | "soft" | "complaint" — soft never sets ContactEmailUnusable.</summary>
    [MaxLength(20)]
    public string? ContactEmailBounceType { get; set; }

    /// <summary>Provider-supplied reason, verbatim from the bounce (truncated).</summary>
    [MaxLength(500)]
    public string? ContactEmailBounceReason { get; set; }

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
