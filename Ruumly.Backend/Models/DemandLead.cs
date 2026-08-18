using System.ComponentModel.DataAnnotations;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

public class DemandLead
{
    public Guid Id { get; set; }
    [MaxLength(200)] public string Email { get; set; } = string.Empty;
    [MaxLength(100)] public string City { get; set; } = string.Empty;
    public DemandLeadCategory Category { get; set; } = DemandLeadCategory.Any;
    [MaxLength(500)] public string? Query { get; set; }
    public string Language { get; set; } = "et";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DemandLeadStatus Status { get; set; } = DemandLeadStatus.New;
    public string? AdminNotes { get; set; }

    // ── Routing + quote lifecycle (moving/trailer "request a quote") ──────────
    // A lead captured from a specific listing is routed to that listing's partner
    // (SupplierId set); generic demand-capture leads leave SupplierId null and stay
    // in the admin-only CRM. The partner responds with a one-time QuotedPrice.
    public Guid? SupplierId { get; set; }
    public Guid? ListingId { get; set; }
    [MaxLength(120)] public string? Name { get; set; }
    [MaxLength(40)]  public string? Phone { get; set; }
    public decimal? QuotedPrice { get; set; }
    public DateTime? QuotedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    [MaxLength(2000)] public string? ProviderNotes { get; set; }

    // ── Concierge intake (demand-first pivot) ─────────────────────────────────
    // A concierge request is a structured need ("move me from City to ToCity on
    // NeedDate") captured before any listing exists for it. Source tags the intake
    // channel ("concierge"); ContactedAt stamps the first admin touch for
    // first-response metrics.
    [MaxLength(100)]  public string? ToCity { get; set; }
    public DateTime? NeedDate { get; set; }
    [MaxLength(2000)] public string? Details { get; set; }
    [MaxLength(40)]   public string? Source { get; set; }
    public DateTime? ContactedAt { get; set; }

    /// <summary>
    /// Marketing attribution as the browser saw it on the visitor's FIRST touch:
    /// utm_source/medium/campaign/term/content, gclid/fbclid, the external
    /// referrer, and the landing path — flattened into one opaque string.
    ///
    /// <see cref="Source"/> already records which FORM produced the lead
    /// ("concierge" vs "routed"); it cannot say which ad, post or search brought
    /// the person to that form. Without this, "cost per qualified request" — a
    /// north-star metric — is not computable, so paid tests cannot be judged and
    /// the demand channels that actually work cannot be told from the ones that
    /// merely look busy.
    ///
    /// Deliberately ONE free-text column rather than five typed ones: nothing
    /// branches on it, it is read by a human (and by exports) and never matched
    /// on, and one nullable column is a far smaller commitment than a schema that
    /// pretends to know which attribution fields matter before any campaign has
    /// run. Never contains personal data — see the frontend collector, which
    /// keeps an allow-list of known marketing parameters rather than copying the
    /// query string.
    /// </summary>
    [MaxLength(300)]  public string? Attribution { get; set; }

    /// <summary>
    /// Private-bucket storage keys for photos the customer attached, as a JSON
    /// array. Empty/null for the overwhelming majority of requests.
    ///
    /// Exists because a provider refused to quote without them: Adduco replied
    /// to a real Haapsalu move with "selle info pealt adekvaatset pakkumist
    /// paraku ei saa teha" and asked for pictures, costing a full round trip on
    /// a job the customer needed that same week. For bulky or awkward loads a
    /// photo is not decoration — it is the difference between a price and a
    /// guess.
    ///
    /// KEYS, never URLs. These objects live in the PRIVATE bucket and have no
    /// public address: they are streamed back only through an endpoint that
    /// checks a credential, because they are pictures of somebody's home.
    ///
    /// Retention is 30 days (LeadPhotoRetentionDays), enforced by a daily purge.
    /// That covers the whole concierge loop with room for a slow provider, and
    /// nothing beyond it — see BackgroundCleanupService.CleanupLeadPhotosAsync.
    /// </summary>
    public string? PhotoKeysJson { get; set; }

    /// <summary>
    /// The intake's scoping answers as submitted, e.g.
    /// <c>{"movingSize":2,"movingAccessOrigin":3,"packingHelp":4}</c>.
    ///
    /// The funnel already asks these as one-tap chips and then throws the
    /// structure away: it renders them to a sentence in the CUSTOMER's language
    /// and glues them into <see cref="Details"/>. Two costs follow. A Russian
    /// customer's answers are pasted verbatim into an Estonian mover's email,
    /// and nothing can be queried -- "moves with no lift" is not a question the
    /// admin can ask.
    ///
    /// Stored as JSON rather than typed columns because Phase 1 is largely about
    /// ADDING questions, and a column per question would mean a migration per
    /// iteration. Labels are resolved at compose time in the RECIPIENT's
    /// language, which is what fixes the leak. Read through the LeadScope helper,
    /// which never throws and drops unknown ids -- same contract as LeadPhotos.
    /// </summary>
    public string? ScopeJson { get; set; }

    /// <summary>
    /// Street address the job starts at, and ends at for a move.
    ///
    /// Never collected before, for any service, at any stage -- so the founder
    /// brokered it by hand on every single job, and a mover could not finalise
    /// anything without a round trip. Adduco's refusal on the Haapsalu move
    /// turned partly on not knowing whether the two ends shared an address.
    ///
    /// NOT shown to a provider before the customer accepts an offer: until then
    /// they see the city, exactly as today. This is somebody's home.
    /// </summary>
    [MaxLength(300)] public string? FromAddress { get; set; }
    [MaxLength(300)] public string? ToAddress   { get; set; }

    /// <summary>
    /// Credential for the customer's own status page, /{lang}/request-status/{token}.
    ///
    /// A concierge customer has no account, so between the receipt email and the
    /// offer email they hear nothing and cannot tell a slow success from a
    /// silent failure -- which mattered enormously while a city-matching bug was
    /// quietly emailing nobody. RequestDetailPage redirects to /account, which
    /// they do not have.
    ///
    /// A token, not the lead id: ids are sequential-ish and enumerable, and this
    /// page shows a stranger what somebody is moving and when their home will be
    /// empty. Nullable because rows created before this feature have no page.
    /// </summary>
    [MaxLength(64)] public string? StatusToken { get; set; }
}
