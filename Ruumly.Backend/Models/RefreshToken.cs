namespace Ruumly.Backend.Models;

public class RefreshToken
{
    public Guid Id { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsRevoked { get; set; }

    /// <summary>
    /// When the token was revoked (UTC), or null if still active. Set alongside every
    /// IsRevoked = true. Used by the refresh-rotation reuse-grace: a token revoked by
    /// normal rotation within the last few seconds is treated as a benign concurrent-
    /// refresh replay rather than a stolen-token reuse.
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// The id of the successor token that replaced this one during normal rotation,
    /// or null. This is the ONLY revocation path that sets it — logout, password-reset,
    /// change-password, account-deletion and blocked-account revocations leave it null,
    /// so the reuse-grace can distinguish "benign concurrent refresh" (replayable) from
    /// "explicitly killed session" (must stay rejected even inside the grace window).
    /// </summary>
    public Guid? ReplacedByTokenId { get; set; }
}
