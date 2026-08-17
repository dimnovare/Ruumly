using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// Turns whatever a visitor uploaded into a photo that is safe to store and to
/// show a stranger.
///
/// This runs on an ANONYMOUS endpoint, so the bytes are entirely untrusted, and
/// what it produces is shown to a provider who is not the customer. Three
/// separate problems, all solved by decoding and re-encoding rather than by
/// inspecting:
///
/// 1. EXIF, and specifically GPS. A phone photo of somebody's living room
///    routinely carries the exact coordinates of their home. Forwarding that to
///    a stranger who was asked to quote a move is a far worse privacy leak than
///    anything else in the concierge loop, and it would be invisible to
///    everyone involved. Re-encoding drops every metadata block, so the stored
///    file cannot carry it.
///
/// 2. "Is this actually an image?" A content-type header is a claim by the
///    caller, and a magic-byte check only reads the first few bytes. Fully
///    decoding the pixels is the only assertion that means anything — a file
///    that is really a script, a zip or a malformed decoder exploit fails here
///    rather than being stored and later served.
///
/// 3. Size. Modern phone photos are 4-12 MB and no provider needs 4032 px to
///    judge a pile of gym equipment. Bounding the long edge keeps the R2 bill
///    and the quote page honest.
///
/// Deliberately re-encodes even when the input is already a clean JPEG: "we
/// re-encode everything" is a property that stays true, whereas "we re-encode
/// when we think we need to" decays into a bypass.
/// </summary>
public static class LeadPhotoNormalizer
{
    /// <summary>Longest edge kept. Enough to judge furniture, far below a phone's native size.</summary>
    public const int MaxEdgePixels = 1600;

    /// <summary>Upper bound on an accepted upload, before decoding.</summary>
    public const long MaxUploadBytes = 8 * 1024 * 1024;

    /// <summary>How many photos one request may carry.</summary>
    public const int MaxPhotosPerLead = 6;

    /// <summary>JPEG quality for the stored copy — visually clean, roughly a tenth the size.</summary>
    private const int Quality = 78;

    /// <summary>The prefix every key this feature creates lives under.</summary>
    public const string KeyPrefix = "lead-photos/";

    /// <summary>
    /// Keep only keys this feature could actually have issued.
    ///
    /// The submitted list arrives from the browser, so without this a caller
    /// could point a lead at ANY object in the private bucket — signed
    /// contracts among them — and then read it back through the quote page,
    /// which serves whatever the lead references. The upload endpoint mints
    /// "lead-photos/{32 hex}.jpg" and nothing else, so anything that does not
    /// match that exact shape was not issued here.
    ///
    /// Also caps the count and de-duplicates: the browser is not the authority
    /// on how many photos a request may carry.
    /// </summary>
    public static List<string> KeepIssuedKeys(IEnumerable<string?>? submitted)
    {
        if (submitted is null) return [];

        var kept = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in submitted)
        {
            if (kept.Count >= MaxPhotosPerLead) break;
            var key = raw?.Trim();
            if (string.IsNullOrEmpty(key) || !IsIssuedKey(key)) continue;
            if (seen.Add(key)) kept.Add(key);
        }

        return kept;
    }

    /// <summary>
    /// Exactly the shape the upload path mints, and nothing else.
    ///
    /// The stored key is NOT simply the filename we asked for: the R2 service
    /// prepends its own <c>yyyy/MM/</c> partition to every object, so what comes
    /// back is <c>2026/08/lead-photos/{32 hex}.jpg</c>. An earlier version of
    /// this check anchored on the prefix at position zero and therefore rejected
    /// every real upload — photos would have been stored successfully and then
    /// silently dropped when attaching them to the lead. Caught against
    /// production, which is the only place the partition shows up.
    ///
    /// So: an OPTIONAL leading <c>yyyy/MM/</c>, then the fixed segment, then 32
    /// lowercase hex and <c>.jpg</c>. Everything is still pinned — no traversal,
    /// no extra segments, no alternative extension — and the local-disk service,
    /// which does not partition, keeps working.
    /// </summary>
    private static bool IsIssuedKey(string key)
    {
        const int hexLength = 32;
        const string suffix = ".jpg";

        var rest = key;

        // Optional yyyy/MM/ partition, matched strictly rather than skipped.
        var firstSlash = rest.IndexOf('/');
        if (firstSlash == 4 && rest.Length > 8
            && rest[5..7].All(char.IsAsciiDigit) && rest[..4].All(char.IsAsciiDigit)
            && rest[7] == '/')
        {
            rest = rest[8..];
        }

        if (!rest.StartsWith(KeyPrefix, StringComparison.Ordinal)) return false;
        if (!rest.EndsWith(suffix, StringComparison.Ordinal)) return false;

        var name = rest[KeyPrefix.Length..^suffix.Length];
        if (name.Length != hexLength) return false;

        foreach (var ch in name)
        {
            var isHex = ch is >= '0' and <= '9' or >= 'a' and <= 'f';
            if (!isHex) return false;
        }
        return true;
    }

    /// <summary>
    /// Decode, strip metadata, bound the dimensions and re-encode as JPEG.
    /// Returns null when the bytes are not a decodable image, which is the only
    /// verdict this class offers — callers must not fall back to trusting the
    /// declared content type.
    /// </summary>
    public static async Task<byte[]?> NormalizeAsync(Stream input, CancellationToken ct = default)
    {
        try
        {
            // Identify+decode. Anything that is not a real raster image throws,
            // including a file whose extension and content-type both lie.
            using var image = await Image.LoadAsync(input, ct);

            // Metadata is dropped rather than edited: an allow-list of "safe"
            // EXIF tags would be a standing invitation to miss one.
            image.Metadata.ExifProfile = null;
            image.Metadata.IptcProfile = null;
            image.Metadata.XmpProfile  = null;
            image.Metadata.IccProfile  = null;

            if (image.Width > MaxEdgePixels || image.Height > MaxEdgePixels)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(MaxEdgePixels, MaxEdgePixels),
                }));
            }

            var output = new MemoryStream();
            await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = Quality }, ct);
            return output.ToArray();
        }
        catch
        {
            // Corrupt, hostile, or simply not an image. The caller reports a
            // generic rejection — echoing decoder detail back to an anonymous
            // caller tells an attacker which inputs get furthest.
            return null;
        }
    }
}
