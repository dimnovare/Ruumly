using System.Security.Cryptography;

namespace Ruumly.Backend.Helpers;

public static class OfferToken
{
    /// <summary>
    /// 32 bytes of cryptographic randomness as url-safe base64 without padding
    /// (RFC 4648 §5): 43 chars of [A-Za-z0-9_-]. Safe to embed directly in
    /// /offer/{token} URLs and email links. Uniqueness is enforced by the DB
    /// index; at 256 bits of entropy a collision is not a practical concern.
    /// </summary>
    public static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}
