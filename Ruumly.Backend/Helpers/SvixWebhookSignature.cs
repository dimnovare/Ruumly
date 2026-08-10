using System.Security.Cryptography;
using System.Text;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// Verifier for the "Standard Webhooks" / Svix signature scheme that Resend uses.
///
/// The endpoint is public and unauthenticated — the signature IS the auth, so
/// this is the only thing standing between an attacker and the ability to mark
/// any provider's email dead. Kept as a pure static function so it can be tested
/// exhaustively without a web host.
///
/// Scheme: signed content is "{id}.{timestamp}.{body}"; the signature is
/// base64(HMAC-SHA256(base64decode(secret without the "whsec_" prefix), content)).
/// The svix-signature header carries a SPACE-separated list of "v1,{signature}"
/// pairs (secret rotation sends more than one) — a match on any v1 entry passes.
/// </summary>
public static class SvixWebhookSignature
{
    /// <summary>Replay window either side of the header timestamp.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    private const string SecretPrefix = "whsec_";

    public enum Result
    {
        Valid,
        MissingHeaders,
        BadTimestamp,
        Expired,
        NoMatch,
        BadSecret,
    }

    /// <param name="id">svix-id / webhook-id header.</param>
    /// <param name="timestamp">svix-timestamp / webhook-timestamp header (unix seconds).</param>
    /// <param name="signatureHeader">svix-signature / webhook-signature header.</param>
    /// <param name="body">The RAW request body, byte-for-byte as received.</param>
    /// <param name="secret">Endpoint secret from the Resend dashboard ("whsec_..." or bare base64).</param>
    /// <param name="now">Injected clock for tests.</param>
    public static Result Verify(
        string? id,
        string? timestamp,
        string? signatureHeader,
        string body,
        string? secret,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(id)
            || string.IsNullOrWhiteSpace(timestamp)
            || string.IsNullOrWhiteSpace(signatureHeader))
            return Result.MissingHeaders;

        if (string.IsNullOrWhiteSpace(secret))
            return Result.BadSecret;

        if (!long.TryParse(timestamp.Trim(), out var unixSeconds))
            return Result.BadTimestamp;

        var sentAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        // Guard both directions: an old capture is a replay, a far-future stamp
        // is an attempt to mint a signature that stays valid forever.
        if (sentAt < now - Tolerance || sentAt > now + Tolerance)
            return Result.Expired;

        if (!TryDecodeSecret(secret, out var key))
            return Result.BadSecret;

        var signedContent = $"{id.Trim()}.{timestamp.Trim()}.{body}";
        var expected = Convert.ToBase64String(
            HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signedContent)));
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        foreach (var part in signatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            // "v1,{base64}" — ignore versions we do not implement rather than failing,
            // so a future v2 alongside v1 still verifies.
            var comma = part.IndexOf(',');
            if (comma < 0) continue;
            if (!part.AsSpan(0, comma).SequenceEqual("v1")) continue;

            var candidate = Encoding.UTF8.GetBytes(part[(comma + 1)..]);
            if (CryptographicOperations.FixedTimeEquals(candidate, expectedBytes))
                return Result.Valid;
        }

        return Result.NoMatch;
    }

    private static bool TryDecodeSecret(string secret, out byte[] key)
    {
        var raw = secret.Trim();
        if (raw.StartsWith(SecretPrefix, StringComparison.Ordinal))
            raw = raw[SecretPrefix.Length..];

        try
        {
            key = Convert.FromBase64String(raw);
        }
        catch (FormatException)
        {
            // Not base64 at all — a misconfigured secret, not a rejected caller.
            key = [];
            return false;
        }

        return key.Length > 0;
    }
}
