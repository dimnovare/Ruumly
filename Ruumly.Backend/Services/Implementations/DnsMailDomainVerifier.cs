using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

/// <summary>
/// <see cref="IMailDomainVerifier"/> over DNS-over-HTTPS (Cloudflare 1.1.1.1).
///
/// DoH rather than a DNS library on purpose: .NET has no MX lookup in the BCL, and
/// plain UDP :53 to an arbitrary resolver is exactly the kind of egress a container
/// platform is liable to drop. DoH is an ordinary HTTPS call to a fixed host, so it
/// works anywhere the app can already reach Resend. It is also what the 2026-08-09
/// audit cross-checked against, so results here match that file's verdicts.
///
/// Registered as a SINGLETON so the cache survives between requests: the intro
/// campaign is dry-run repeatedly before it is ever sent, and re-resolving ~500
/// domains on each preview would be both slow and rude to the resolver.
/// </summary>
public sealed class DnsMailDomainVerifier(
    IHttpClientFactory httpFactory, ILogger<DnsMailDomainVerifier> logger) : IMailDomainVerifier
{
    /// <summary>Named HttpClient registration (see Program.cs).</summary>
    public const string HttpClientName = "mail-domain-verifier";

    private const string ResolverUrl = "https://cloudflare-dns.com/dns-query";

    /// <summary>Max concurrent lookups. ~500 domains at 12-wide finishes in seconds.</summary>
    private const int MaxConcurrency = 12;

    /// <summary>
    /// How long a decided verdict is trusted. Domains lapse, but not within a
    /// campaign preview session, and the audit's own advice is to re-sweep
    /// periodically rather than continuously.
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(12);

    /// <summary>
    /// An INCONCLUSIVE result is cached only briefly. Caching a resolver blip for
    /// 12 hours would make a transient failure look like a settled answer.
    /// </summary>
    private static readonly TimeSpan InconclusiveCacheTtl = TimeSpan.FromMinutes(1);

    internal enum Verdict { Deliverable, Undeliverable, Inconclusive }

    private readonly ConcurrentDictionary<string, (Verdict Verdict, DateTimeOffset ExpiresAt)> cache =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlySet<string>> FindUndeliverableAsync(
        IEnumerable<string> domains, CancellationToken ct = default)
    {
        var distinct = domains
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinct.Count == 0)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var undeliverable = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var inconclusive  = 0;

        using var http = httpFactory.CreateClient(HttpClientName);
        using var gate = new SemaphoreSlim(MaxConcurrency);
        var lookups = distinct.Select(async domain =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var verdict = await VerdictForAsync(http, domain, ct);
                if (verdict == Verdict.Undeliverable) undeliverable.TryAdd(domain, 0);
                if (verdict == Verdict.Inconclusive) Interlocked.Increment(ref inconclusive);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(lookups);

        if (inconclusive > 0)
        {
            // Loud, because these suppliers WILL be mailed. Failing open is the
            // deliberate choice (see IMailDomainVerifier), but it must be visible.
            logger.LogWarning(
                "Mail-domain check: {Inconclusive} of {Total} domain(s) could not be resolved and were " +
                "treated as deliverable. {Undeliverable} domain(s) proven undeliverable.",
                inconclusive, distinct.Count, undeliverable.Count);
        }

        return new HashSet<string>(undeliverable.Keys, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Verdict> VerdictForAsync(HttpClient http, string domain, CancellationToken ct)
    {
        if (cache.TryGetValue(domain, out var hit) && hit.ExpiresAt > DateTimeOffset.UtcNow)
            return hit.Verdict;

        var verdict = await ResolveAsync(http, domain, ct);

        cache[domain] = (verdict, DateTimeOffset.UtcNow.Add(
            verdict == Verdict.Inconclusive ? InconclusiveCacheTtl : CacheTtl));

        return verdict;
    }

    private async Task<Verdict> ResolveAsync(HttpClient http, string domain, CancellationToken ct)
    {
        // The domain comes from a scraped address, so it is untrusted input that is
        // about to be interpolated into a URL. Normalise to punycode and refuse
        // anything that is not a plain LDH hostname before it gets near the query
        // string; an unqueryable name is INCONCLUSIVE, never a verdict against the
        // provider.
        var ascii = ToAsciiHostname(domain);
        if (ascii is null)
            return Verdict.Inconclusive;

        var url = $"{ResolverUrl}?name={Uri.EscapeDataString(ascii)}&type=MX";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-json"));

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return Verdict.Inconclusive;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            return Interpret(json.RootElement);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Timeout, DNS failure, malformed body — all mean "we do not know".
            logger.LogDebug(ex, "MX lookup for {Domain} failed; treating as inconclusive.", domain);
            return Verdict.Inconclusive;
        }
    }

    /// <summary>
    /// Reads a DoH JSON answer. NXDOMAIN (status 3) and NOERROR-with-no-MX are both
    /// undeliverable; any other status is inconclusive.
    ///
    /// NOERROR-with-no-MX is treated as a hard failure even though RFC 5321 allows an
    /// implicit MX at the A record. On this directory that call is safe and correct:
    /// all three such domains in the audit refused TCP :25 outright, and two of them
    /// pointed at Cloudflare proxy IPs, which never run SMTP.
    /// </summary>
    internal static Verdict Interpret(JsonElement root)
    {
        if (!root.TryGetProperty("Status", out var statusElement) ||
            statusElement.ValueKind != JsonValueKind.Number)
            return Verdict.Inconclusive;

        var status = statusElement.GetInt32();

        const int NoError  = 0;
        const int NxDomain = 3;

        if (status == NxDomain) return Verdict.Undeliverable;
        if (status != NoError)  return Verdict.Inconclusive;

        if (!root.TryGetProperty("Answer", out var answers) ||
            answers.ValueKind != JsonValueKind.Array)
            return Verdict.Undeliverable;   // resolves, publishes no MX

        var hasUsableMx = false;
        foreach (var answer in answers.EnumerateArray())
        {
            const int MxRecordType = 15;
            if (!answer.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.Number ||
                type.GetInt32() != MxRecordType)
                continue;

            var data = answer.TryGetProperty("data", out var d) ? d.GetString() : null;
            if (!IsNullMx(data)) hasUsableMx = true;
        }

        return hasUsableMx ? Verdict.Deliverable : Verdict.Undeliverable;
    }

    /// <summary>
    /// RFC 7505 "null MX" — a single record of preference 0 pointing at the root
    /// ("0 ."). It is a domain explicitly declaring that it accepts no mail, so it
    /// counts as undeliverable rather than as a usable exchanger.
    /// </summary>
    private static bool IsNullMx(string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return true;

        var parts = data.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return true;

        var target = parts[^1].Trim();
        return target is "." or "";
    }

    /// <summary>
    /// Punycode-normalises a hostname and enforces LDH syntax (letters, digits,
    /// hyphen; labels 1-63 chars, no leading/trailing hyphen; at least two labels;
    /// 253 chars total). Returns null when the name cannot be queried safely.
    /// </summary>
    internal static string? ToAsciiHostname(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return null;

        var value = domain.Trim().TrimEnd('.');
        if (value.Length == 0 || value.Length > 253) return null;

        string ascii;
        try
        {
            ascii = new IdnMapping { AllowUnassigned = true }.GetAscii(value).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (ascii.Length is 0 or > 253) return null;

        var labels = ascii.Split('.');
        if (labels.Length < 2) return null;

        foreach (var label in labels)
        {
            if (label.Length is 0 or > 63) return null;
            if (label[0] == '-' || label[^1] == '-') return null;

            foreach (var c in label)
                if (!char.IsAsciiLetterOrDigit(c) && c != '-')
                    return null;
        }

        return ascii;
    }
}
