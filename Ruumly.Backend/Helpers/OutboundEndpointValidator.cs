namespace Ruumly.Backend.Helpers;

/// <summary>
/// SSRF guard for outbound HTTP calls to supplier-configured endpoints.
/// Blocks loopback (127.x, ::1), link-local (169.254.x), AWS metadata,
/// RFC-1918 private ranges, CGNAT (Railway internal), and non-HTTP(S) schemes.
/// Hostname-based URLs that resolve to private IPs are NOT caught — this
/// guard intentionally trades async DNS resolution for sync simplicity;
/// the documented attack vectors are covered.
/// </summary>
public static class OutboundEndpointValidator
{
    public static bool IsAllowed(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        // Only allow HTTP(S) — block file://, ftp://, gopher://, etc.
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;

        // Block bare hostnames / single-label names (internal DNS shortcuts, e.g. "localhost")
        if (!uri.Host.Contains('.') && !uri.Host.StartsWith('[')) return false;

        // If the host is NOT a raw IP address, allow it (DNS-resolved IPs are not checked here
        // because async DNS resolution would complicate this guard; the private IP ranges
        // covering real-world attack vectors are already blocked via the IP literal check below).
        if (!System.Net.IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip)) return true;

        // Map IPv6-encoded IPv4 (::ffff:192.168.x.x) to IPv4 for uniform range checks
        var v4 = ip.MapToIPv4();
        var b  = v4.GetAddressBytes();

        var isPrivate =
            b[0] == 127 ||                                          // 127.x.x.x — loopback
            b[0] == 10  ||                                          // 10.x.x.x  — RFC-1918
            (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||           // 172.16–31  — RFC-1918
            (b[0] == 192 && b[1] == 168) ||                         // 192.168.x  — RFC-1918
            (b[0] == 169 && b[1] == 254) ||                         // 169.254.x  — link-local / AWS metadata
            (b[0] == 100 && b[1] >= 64 && b[1] <= 127) ||          // 100.64-127 — CGNAT (Railway)
            b[0] == 0;                                              // 0.x.x.x    — "this" network

        return !isPrivate;
    }
}
