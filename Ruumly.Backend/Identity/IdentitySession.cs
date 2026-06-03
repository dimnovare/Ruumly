namespace Ruumly.Backend.Identity;

/// <summary>
/// Represents an in-progress identity verification session with a provider.
///
/// <para>
/// <see cref="SessionId"/> is the opaque handle returned by the provider (e.g. the
/// Smart-ID or Mobile-ID session id) that is used to poll for the session outcome.
/// </para>
///
/// <para>
/// <see cref="VerificationCode"/> is the 4-digit anti-phishing code derived from the
/// authentication hash. The customer's app displays the same code; the customer must
/// confirm both codes match before approving the sign request.
/// </para>
/// </summary>
/// <param name="SessionId">Opaque session identifier from the SK RP-API.</param>
/// <param name="VerificationCode">4-digit verification code (e.g. "2847") — show to the user before they approve in the app.</param>
/// <param name="Provider">Provider name ("smart-id" or "mobile-id").</param>
/// <param name="ExpiresAt">When the provider session expires (UTC). The client should stop polling after this time.</param>
public sealed record IdentitySession(
    string SessionId,
    string VerificationCode,
    string Provider,
    DateTime ExpiresAt);
