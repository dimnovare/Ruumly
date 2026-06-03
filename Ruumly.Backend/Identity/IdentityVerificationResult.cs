namespace Ruumly.Backend.Identity;

/// <summary>
/// The result returned by <see cref="IIdentityVerificationProvider.PollSessionAsync"/>
/// once a session reaches a terminal state.
///
/// <para>
/// When <see cref="Status"/> is <see cref="IdentityVerificationStatus.Verified"/>, the
/// caller should write <see cref="VerifiedName"/>, <see cref="PersonalCodeHash"/>,
/// <see cref="Country"/> and <see cref="CertificateInfo"/> to the signed contract row.
/// Raw personal codes are NEVER exposed here — only the HMAC-SHA256 hash.
/// </para>
/// </summary>
/// <param name="Status">Terminal status of the session.</param>
/// <param name="VerifiedName">Full name from the certificate Subject CN. Null for non-Verified outcomes.</param>
/// <param name="PersonalCodeHash">HMAC-SHA256 of the raw personal code, keyed with <c>Identity:HmacSecret</c>. Null for non-Verified outcomes.</param>
/// <param name="Country">"EE", "LV", or "LT" parsed from the ETSI personal-code prefix. Null for non-Verified outcomes.</param>
/// <param name="CertificateInfo">JSON summary of the signer certificate (issuer, serial, expiry). Raw cert bytes are never included.</param>
/// <param name="FailureReason">Human-readable reason when <see cref="Status"/> is Failed or Expired.</param>
public sealed record IdentityVerificationResult(
    IdentityVerificationStatus Status,
    string? VerifiedName = null,
    string? PersonalCodeHash = null,
    string? Country = null,
    string? CertificateInfo = null,
    string? FailureReason = null);
