using System.Security.Cryptography;
using System.Text;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// Credentials for the public "claim your profile" flow: the emailed magic link
/// and the claim session it mints.
///
/// Deliberately assembled from the two patterns already in this codebase rather
/// than a third invention:
/// <list type="bullet">
/// <item>MINTING reuses <see cref="OfferToken.Generate"/> — 256 bits of
/// randomness as 43 url-safe base64 chars, the same shape as the offer token and
/// ProviderOutreach.QuoteToken, so it drops straight into an email URL.</item>
/// <item>STORAGE is SHA-256 hex, the same as <c>RefreshToken.TokenHash</c> and
/// the password-reset / email-verification tokens in AuthService. The quote
/// token is stored raw because it grants only "submit a price for this one
/// lead"; a claim token grants EDIT rights over a public business profile, so it
/// is held to the refresh-token standard: the database never contains a usable
/// credential.</item>
/// </list>
/// The hash is unsalted and unstretched on purpose — these are 256-bit random
/// tokens, not passwords, so there is no dictionary to run and a fast digest is
/// what lets the lookup be a plain indexed equality match.
/// </summary>
public static class ClaimToken
{
    /// <summary>
    /// How long an emailed magic link stays redeemable. Long enough that a
    /// provider who reads mail the next morning is not locked out, short enough
    /// that a link forwarded or left in a shared inbox goes stale quickly.
    /// </summary>
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromHours(24);

    /// <summary>
    /// How long a redeemed claim session stays valid. Starts at redemption, not
    /// at request, so a provider who clicks late still gets a full working day
    /// to finish editing.
    /// </summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);

    /// <summary>256-bit url-safe token — the raw value only ever leaves in an email.</summary>
    public static string Generate() => OfferToken.Generate();

    /// <summary>
    /// SHA-256 hex, lower-cased. Byte-identical to AuthService.HashToken so the
    /// two token stores stay comparable.
    /// </summary>
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
