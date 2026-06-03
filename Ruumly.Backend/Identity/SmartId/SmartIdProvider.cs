using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Ruumly.Backend.Identity.SmartId;

/// <summary>
/// <see cref="IIdentityVerificationProvider"/> implementation for SK Smart-ID RP-API.
///
/// <para>
/// <strong>Authentication flow:</strong>
/// <list type="number">
///   <item>Generate 64 random bytes and compute their SHA-256 hash (the "auth hash").</item>
///   <item>POST to <c>/authentication/etsi/PNO{country}-{personalCode}</c> with the
///   base64-encoded hash. Returns a <c>sessionID</c>.</item>
///   <item>Derive the 4-digit verification code from the first two bytes of the auth hash:
///   <c>((hash[0] &amp; 0xFF) &lt;&lt; 8 | (hash[1] &amp; 0xFF)) % 10000</c>.
///   Display this to the user — the Smart-ID app shows the same code for the user to confirm.</item>
///   <item>Poll <c>GET /session/{sessionId}?timeoutMs=10000</c> until <c>state == "COMPLETE"</c>.</item>
///   <item>On <c>endResult == "OK"</c>, parse the DER certificate from <c>cert.value</c> (base64) to
///   extract identity — raw cert bytes are NEVER persisted, only a JSON summary.</item>
/// </list>
/// </para>
///
/// <para>
/// Env-gated: with <c>SmartId:RelyingPartyUuid</c> empty, <see cref="IsConfiguredAsync"/>
/// returns <c>false</c> and no network calls are made.
/// </para>
/// </summary>
public sealed class SmartIdProvider : IIdentityVerificationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http;
    private readonly SmartIdConfig _config;
    private readonly ILogger<SmartIdProvider> _logger;
    private readonly byte[] _hmacKey;

    public SmartIdProvider(
        HttpClient http,
        IOptions<SmartIdConfig> config,
        ILogger<SmartIdProvider> logger)
    {
        _http = http;
        _config = config.Value;
        _logger = logger;

        // Use Identity:HmacSecret (falls back to a dev placeholder — never persist without a real secret).
        var secret = !string.IsNullOrWhiteSpace(_config.HmacSecret)
            ? _config.HmacSecret
            : "ruumly-dev-identity-hmac-secret-change-me-0001";
        _hmacKey = Encoding.UTF8.GetBytes(secret);

        _http.BaseAddress = new Uri(_config.BaseUrl.TrimEnd('/') + '/');
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public string ProviderName => "smart-id";

    public Task<bool> IsConfiguredAsync() => Task.FromResult(_config.IsConfigured);

    /// <inheritdoc/>
    public async Task<IdentitySession> StartAuthenticationAsync(
        string personalCode,
        string country,
        string? phoneNumber = null,
        CancellationToken ct = default)
    {
        if (!_config.IsConfigured)
        {
            throw new InvalidOperationException(
                "Smart-ID is not configured. Set SmartId:RelyingPartyUuid.");
        }

        // Generate 64 random bytes; compute SHA-256 hash (the "auth hash").
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        var authHash = SHA256.HashData(randomBytes);
        var hashBase64 = Convert.ToBase64String(authHash);

        // 4-digit verification code: first two bytes of the auth hash, big-endian modulo 10000.
        var vcInt = ((authHash[0] & 0xFF) << 8 | (authHash[1] & 0xFF)) % 10000;
        var verificationCode = vcInt.ToString("D4");

        var url = $"authentication/etsi/PNO{country.ToUpperInvariant()}-{personalCode}";

        var requestBody = new
        {
            relyingPartyUUID = _config.RelyingPartyUuid,
            relyingPartyName = _config.RelyingPartyName,
            hash = hashBase64,
            hashType = "SHA256",
            allowedInteractionsOrder = new[]
            {
                new
                {
                    type = "displayTextAndPIN",
                    displayText60 = $"Verify identity for {_config.RelyingPartyName} rental",
                },
            },
        };

        using var response = await _http.PostAsJsonAsync(url, requestBody, JsonOptions, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Smart-ID returned {StatusCode} starting session for PNO{Country}-[redacted]: {Body}",
                (int)response.StatusCode, country, body);
            throw new InvalidOperationException(
                $"Smart-ID session start failed with HTTP {(int)response.StatusCode}.");
        }

        var parsed = JsonDocument.Parse(body);
        var sessionId = parsed.RootElement.GetProperty("sessionID").GetString()
            ?? throw new InvalidOperationException("Smart-ID start response contained no sessionID.");

        _logger.LogInformation(
            "Smart-ID session {SessionId} started for PNO{Country} (vc={VerificationCode}).",
            sessionId, country, verificationCode);

        return new IdentitySession(
            SessionId: sessionId,
            VerificationCode: verificationCode,
            Provider: ProviderName,
            ExpiresAt: DateTime.UtcNow + SessionLifetime);
    }

    /// <inheritdoc/>
    public async Task<IdentityVerificationResult> PollSessionAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        var url = $"session/{Uri.EscapeDataString(sessionId)}?timeoutMs=10000";

        try
        {
            using var response = await _http.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Smart-ID returned {StatusCode} polling session {SessionId}: {Body}",
                    (int)response.StatusCode, sessionId, body);
                return new IdentityVerificationResult(
                    IdentityVerificationStatus.Failed,
                    FailureReason: $"HTTP {(int)response.StatusCode} from Smart-ID");
            }

            var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var state = root.TryGetProperty("state", out var stateEl)
                ? stateEl.GetString()?.ToUpperInvariant()
                : null;

            if (state == "RUNNING")
            {
                return new IdentityVerificationResult(IdentityVerificationStatus.Running);
            }

            if (state != "COMPLETE")
            {
                _logger.LogWarning(
                    "Smart-ID session {SessionId} returned unexpected state '{State}'.",
                    sessionId, state);
                return new IdentityVerificationResult(
                    IdentityVerificationStatus.Failed,
                    FailureReason: $"Unexpected state: {state}");
            }

            // state == COMPLETE; inspect endResult
            var endResult = root.TryGetProperty("result", out var resultEl) &&
                            resultEl.TryGetProperty("endResult", out var er)
                ? er.GetString()?.ToUpperInvariant()
                : null;

            if (endResult != "OK")
            {
                var (status, reason) = MapEndResult(endResult);
                _logger.LogInformation(
                    "Smart-ID session {SessionId} completed with endResult '{EndResult}'.",
                    sessionId, endResult);
                return new IdentityVerificationResult(status, FailureReason: reason);
            }

            // endResult == OK: extract identity from the certificate
            if (!root.TryGetProperty("cert", out var certEl) ||
                !certEl.TryGetProperty("value", out var certValue))
            {
                _logger.LogWarning(
                    "Smart-ID session {SessionId} completed OK but cert.value was absent.", sessionId);
                return new IdentityVerificationResult(
                    IdentityVerificationStatus.Failed,
                    FailureReason: "Certificate missing from Smart-ID response");
            }

            var certBase64 = certValue.GetString() ?? "";
            var certBytes = Convert.FromBase64String(certBase64);
            return ParseCertificate(certBytes, sessionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Exception polling Smart-ID session {SessionId}.", sessionId);
            return new IdentityVerificationResult(
                IdentityVerificationStatus.Failed,
                FailureReason: "Network error polling Smart-ID");
        }
    }

    // ─── Certificate parsing ──────────────────────────────────────────────────

    private IdentityVerificationResult ParseCertificate(byte[] certBytes, string sessionId)
    {
        try
        {
            using var cert = new X509Certificate2(certBytes);

            // Subject CN holds the full name in Smart-ID certs (e.g. "MARI-LIIS MÄNNIK").
            var verifiedName = GetSubjectComponent(cert, "CN");

            // SERIALNUMBER holds the ETSI personal code, e.g. "PNOEE-38001085718".
            var serialNumber = GetSubjectComponent(cert, "SERIALNUMBER")
                ?? GetSubjectComponent(cert, "OID.2.5.4.5");

            var (country, personalCodeHash) = ParseEtsiPersonalCode(serialNumber);

            var certInfo = JsonSerializer.Serialize(new
            {
                issuer = cert.Issuer,
                serial = cert.SerialNumber,
                notAfter = cert.NotAfter.ToString("O"),
                subject = cert.Subject,
            });

            _logger.LogInformation(
                "Smart-ID session {SessionId} verified: name='{Name}', country={Country}.",
                sessionId, verifiedName, country);

            return new IdentityVerificationResult(
                Status: IdentityVerificationStatus.Verified,
                VerifiedName: verifiedName,
                PersonalCodeHash: personalCodeHash,
                Country: country,
                CertificateInfo: certInfo);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse certificate for session {SessionId}.", sessionId);
            return new IdentityVerificationResult(
                IdentityVerificationStatus.Failed,
                FailureReason: "Certificate parsing failed");
        }
    }

    /// <summary>
    /// Parses an ETSI personal-code string like "PNOEE-38001085718" into (country, hmac-hash).
    /// Returns (null, null) when the format is unrecognised.
    /// </summary>
    private (string? Country, string? Hash) ParseEtsiPersonalCode(string? etsiCode)
    {
        if (string.IsNullOrWhiteSpace(etsiCode)) return (null, null);

        const string prefix = "PNO";
        if (!etsiCode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || etsiCode.Length < 6)
            return (null, null);

        var dashIndex = etsiCode.IndexOf('-', prefix.Length);
        if (dashIndex < 0 || dashIndex + 1 >= etsiCode.Length) return (null, null);

        var country = etsiCode[prefix.Length..dashIndex].ToUpperInvariant();
        var personalCode = etsiCode[(dashIndex + 1)..];

        // HMAC-SHA256 of the raw personal code; raw code is never returned/stored.
        using var hmac = new HMACSHA256(_hmacKey);
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(personalCode));
        return (country, Convert.ToBase64String(hashBytes));
    }

    private static string? GetSubjectComponent(X509Certificate2 cert, string oid)
    {
        foreach (var part in cert.Subject.Split([',', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            var eqIdx = trimmed.IndexOf('=');
            if (eqIdx < 0) continue;
            var key = trimmed[..eqIdx].Trim();
            var value = trimmed[(eqIdx + 1)..].Trim();
            if (string.Equals(key, oid, StringComparison.OrdinalIgnoreCase))
                return value;
        }
        return null;
    }

    private static (IdentityVerificationStatus Status, string Reason) MapEndResult(string? endResult) =>
        endResult switch
        {
            "TIMEOUT" => (IdentityVerificationStatus.Expired, "Session timed out before the user responded"),
            "USER_REFUSED" => (IdentityVerificationStatus.Failed, "User refused in the Smart-ID app"),
            "USER_REFUSED_DISPLAYTEXTANDPIN" => (IdentityVerificationStatus.Failed, "User refused the displayTextAndPIN interaction"),
            "USER_REFUSED_VC_CHOICE" => (IdentityVerificationStatus.Failed, "User refused VC choice"),
            "USER_REFUSED_CONFIRMATIONMESSAGE" => (IdentityVerificationStatus.Failed, "User refused the confirmation message"),
            "WRONG_VC" => (IdentityVerificationStatus.Failed, "User chose the wrong verification code"),
            "REQUIRED_INTERACTION_NOT_SUPPORTED_BY_APP" => (IdentityVerificationStatus.Failed, "Smart-ID app does not support the required interaction"),
            _ => (IdentityVerificationStatus.Failed, $"Smart-ID end result: {endResult ?? "unknown"}"),
        };
}
