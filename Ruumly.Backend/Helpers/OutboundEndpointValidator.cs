using System.Net;
using System.Net.Sockets;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// SSRF guard for outbound HTTP calls to supplier-configured endpoints.
/// Blocks loopback (127.x, ::1), link-local (169.254.x), AWS/GCP metadata,
/// RFC-1918 private ranges, CGNAT (Railway internal), unique-local IPv6, and
/// non-HTTP(S) schemes.
///
/// Two entry points:
///   • <see cref="IsAllowed"/>      — synchronous, IP-literal + scheme only. Use at
///                                     config-save time (cheap, no network).
///   • <see cref="IsAllowedAsync"/> — resolves the host via DNS and rejects if ANY
///                                     resolved address is private. Use immediately
///                                     before an outbound request to catch hostnames
///                                     that resolve to private/metadata IPs.
/// </summary>
public static class OutboundEndpointValidator
{
    /// <summary>
    /// Synchronous check: validates scheme, single-label hosts, and IP literals.
    /// Does NOT resolve hostnames. Safe for config-save-time validation.
    /// </summary>
    public static bool IsAllowed(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        // Only allow HTTP(S) — block file://, ftp://, gopher://, etc.
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;

        // Block bare hostnames / single-label names (internal DNS shortcuts, e.g. "localhost")
        if (!uri.Host.Contains('.') && !uri.Host.StartsWith('[')) return false;

        // If the host is NOT a raw IP address, allow it here. DNS-resolved IPs are not
        // checked synchronously; IsAllowedAsync performs the resolve-and-check pass that
        // catches hostnames pointing at private/metadata addresses.
        if (!IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip)) return true;

        return !IsPrivateAddress(ip);
    }

    /// <summary>
    /// Asynchronous check: everything <see cref="IsAllowed"/> does, plus DNS resolution.
    /// If the host is a hostname, every resolved A/AAAA record must be public; if ANY
    /// resolves to a private/loopback/link-local/unique-local/CGNAT/metadata address the
    /// URL is rejected. Call this immediately before the outbound request (DNS-rebind safe
    /// for that single resolution; the HttpClient then dials the same name).
    /// </summary>
    public static async Task<bool> IsAllowedAsync(string? url, CancellationToken ct = default)
    {
        // Scheme / single-label / IP-literal checks first (no network for those).
        if (!IsAllowed(url)) return false;

        // IsAllowed already returned true; re-parse to inspect the host.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        var host = uri.Host.Trim('[', ']');

        // IP literal — already fully validated by IsAllowed, no DNS needed.
        if (IPAddress.TryParse(host, out _)) return true;

        // Hostname — resolve and reject if ANY address is private.
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, ct);
        }
        catch (Exception)
        {
            // Resolution failed (NXDOMAIN, timeout, etc.) — treat as not allowed.
            return false;
        }

        if (addresses.Length == 0) return false;

        foreach (var addr in addresses)
        {
            if (IsPrivateAddress(addr)) return false;
        }

        return true;
    }

    /// <summary>
    /// True if <paramref name="ip"/> is in a loopback, private, link-local, unique-local,
    /// CGNAT, metadata, or unspecified range. Handles both IPv4 and IPv6 (including
    /// IPv4-mapped IPv6).
    /// </summary>
    private static bool IsPrivateAddress(IPAddress ip)
    {
        // Native IPv6 must be range-checked in v6 form. MapToIPv4() on a non-v4-mapped v6
        // address (e.g. ULA fd00::) produces meaningless bytes, so private v6 targets would
        // otherwise slip through (e.g. http://[fd00::808:808]/ maps to 8.8.8.8 and would be
        // wrongly ALLOWED).
        if (ip.AddressFamily == AddressFamily.InterNetworkV6 && !ip.IsIPv4MappedToIPv6)
        {
            var v6 = ip.GetAddressBytes();
            return
                IPAddress.IsLoopback(ip) ||          // ::1 — loopback
                ip.IsIPv6LinkLocal ||                // fe80::/10 — link-local
                ip.IsIPv6SiteLocal ||                // fec0::/10 — deprecated site-local
                (v6[0] & 0xFE) == 0xFC ||            // fc00::/7  — unique local address (ULA)
                ip.Equals(IPAddress.IPv6Any);        // ::        — unspecified
        }

        // Map IPv6-encoded IPv4 (::ffff:192.168.x.x) to IPv4 for uniform range checks.
        var v4 = ip.MapToIPv4();
        var b  = v4.GetAddressBytes();

        return
            b[0] == 127 ||                                 // 127.x.x.x — loopback
            b[0] == 10  ||                                 // 10.x.x.x  — RFC-1918
            (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||  // 172.16–31  — RFC-1918
            (b[0] == 192 && b[1] == 168) ||                // 192.168.x  — RFC-1918
            (b[0] == 169 && b[1] == 254) ||                // 169.254.x  — link-local / cloud metadata (169.254.169.254)
            (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || // 100.64-127 — CGNAT (Railway)
            b[0] == 0;                                     // 0.x.x.x    — "this" network
    }
}
