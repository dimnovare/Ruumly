namespace Ruumly.Backend.Identity;

/// <summary>
/// Pluggable identity verification provider abstraction for SK Smart-ID and Mobile-ID.
///
/// <para>
/// <strong>Env-gating pattern:</strong><br/>
/// When the provider credentials are absent from configuration,
/// <see cref="IsConfiguredAsync"/> returns <c>false</c> and the integration is
/// automatically disabled. Callers check this before starting a session so the API
/// can surface a clear "not configured" response rather than failing at the network
/// call level.
/// </para>
///
/// <para>
/// <strong>Polling model:</strong><br/>
/// <see cref="PollSessionAsync"/> makes one request to the provider and returns the
/// current state. The caller is responsible for the polling loop (typically every 2 s
/// in a frontend-driven flow).
/// </para>
/// </summary>
public interface IIdentityVerificationProvider
{
    /// <summary>Provider key, e.g. "smart-id" or "mobile-id".</summary>
    string ProviderName { get; }

    /// <summary>
    /// Returns <c>true</c> only when real provider credentials are present in configuration.
    /// When <c>false</c> the integration is disabled and no network calls will be made.
    /// </summary>
    Task<bool> IsConfiguredAsync();

    /// <summary>
    /// Opens a new verification session with the provider for the given subject.
    /// Returns an <see cref="IdentitySession"/> carrying the session id, the 4-digit
    /// anti-phishing verification code, and an expiry time.
    /// </summary>
    /// <param name="personalCode">National personal identification code (e.g. "38001085718").</param>
    /// <param name="country">Two-letter country code: "EE", "LV", or "LT".</param>
    /// <param name="phoneNumber">Phone number in international format (required for Mobile-ID).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IdentitySession> StartAuthenticationAsync(
        string personalCode,
        string country,
        string? phoneNumber = null,
        CancellationToken ct = default);

    /// <summary>
    /// Makes a single poll request to the provider and returns the current session state.
    /// Caller loops on <see cref="IdentityVerificationStatus.Running"/> (and
    /// <see cref="IdentityVerificationStatus.Pending"/>) until a terminal state is reached.
    /// </summary>
    Task<IdentityVerificationResult> PollSessionAsync(string sessionId, CancellationToken ct = default);
}
