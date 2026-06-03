namespace Ruumly.Backend.Identity;

/// <summary>
/// The state of a provider session as returned by a single
/// <see cref="IIdentityVerificationProvider.PollSessionAsync"/> call.
/// </summary>
public enum IdentityVerificationStatus
{
    /// <summary>Session was created but the user has not yet acted in their app.</summary>
    Pending,

    /// <summary>The provider is processing the user's response (transient — keep polling).</summary>
    Running,

    /// <summary>The user approved and the certificate is available.</summary>
    Verified,

    /// <summary>The user declined, entered the wrong VC, or a provider error occurred.</summary>
    Failed,

    /// <summary>The provider session expired before the user responded.</summary>
    Expired,
}
