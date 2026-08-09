using System.ComponentModel.DataAnnotations;

namespace Ruumly.Backend.Models;

/// <summary>
/// One "claim your profile" attempt against a directory <see cref="Supplier"/>.
///
/// The directory was built from public research: 1,186 rows with a scraped
/// <see cref="Supplier.ContactEmail"/> and no user account. A provider who gets
/// the introduction campaign opens /{lang}/claim/{slug}, types their business
/// email, and — ONLY when that address matches the stored one — receives a
/// tokenized magic link at that same address. Redeeming it mints a claim
/// session scoped to exactly one supplier id.
///
/// Two rules this row exists to enforce, both security-critical:
/// <list type="bullet">
/// <item>The stored contact email is NEVER echoed to an unauthenticated
/// visitor. A no-match request still writes a row (<see cref="EmailMatched"/>
/// false) and alerts ops, so the HTTP response is byte-identical either way and
/// the endpoint cannot be used to test whether an address is on file — scraped
/// contact data is exactly what a scraper would want back.</item>
/// <item>Both tokens are stored SHA-256 hashed (same as
/// <c>RefreshToken.TokenHash</c>), so a database leak yields no usable
/// credential. The raw token exists only in the email and in the URL the
/// provider clicks.</item>
/// </list>
/// </summary>
public class SupplierClaim
{
    public Guid Id { get; set; }

    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>
    /// The address the visitor typed, lower-cased and trimmed. Kept even when it
    /// does not match, because that is the row ops reviews manually.
    /// </summary>
    [MaxLength(200)] public string RequestedEmail { get; set; } = string.Empty;

    /// <summary>
    /// Whether <see cref="RequestedEmail"/> equalled the supplier's stored
    /// ContactEmail case-insensitively. False rows never carry a token — they
    /// exist only as an ops-review trail.
    /// </summary>
    public bool EmailMatched { get; set; }

    /// <summary>SHA-256 hex of the emailed magic-link token. Null on a no-match row.</summary>
    [MaxLength(64)] public string? TokenHash { get; set; }

    /// <summary>Absolute expiry of the magic link (24 h from the request).</summary>
    public DateTime? TokenExpiresAt { get; set; }

    /// <summary>
    /// When the magic link was redeemed. Non-null means SPENT: the link is
    /// single-use and a second redemption is indistinguishable from an unknown
    /// token (404).
    /// </summary>
    public DateTime? VerifiedAt { get; set; }

    /// <summary>SHA-256 hex of the claim-session token minted at redemption.</summary>
    [MaxLength(64)] public string? SessionTokenHash { get; set; }

    /// <summary>Absolute expiry of the claim session (24 h from redemption).</summary>
    public DateTime? SessionExpiresAt { get; set; }

    /// <summary>Requesting IP, for abuse review of no-match rows.</summary>
    [MaxLength(64)] public string? RequestIp { get; set; }

    /// <summary>UI language of the request — picks the magic-link email language.</summary>
    [MaxLength(5)] public string Language { get; set; } = "et";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
