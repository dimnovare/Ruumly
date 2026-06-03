using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Ruumly.Backend.Identity.SmartId;

namespace Ruumly.Backend.Identity.MobileId;

/// <summary>
/// <see cref="IIdentityVerificationProvider"/> implementation for SK Mobile-ID RP-API.
///
/// <para>
/// <strong>Authentication flow:</strong>
/// <list type="number">
///   <item>Generate 64 random bytes and compute their SHA-256 hash (the "auth hash").</item>
///   <item>POST to <c>/authentication</c> with phone number, ETSI semantic identifier,
///   base64-encoded hash, and language. Returns a <c>sessionID</c>.</item>
///   <item>Derive the 4-digit verification code from the first two bytes of the auth hash.</item>
///   <item>Poll <c>GET /authentication/session/{sessionId}?timeoutMs=10000</c> until
///   <c>state == "COMPLETE"</c>.</item>
///   <item>On <c>endResult == "OK"</c>, parse the DER certificate to extract identity.</item>
/// </list>
/// </para>
///
/// <para>
/// Mobile-ID shares the same SK account/credentials as Smart-ID.
/// Env-gated: disabled when <c>SmartId:RelyingPartyUuid</c> is empty.
/// </para>
/// </summary>
public sealed class MobileIdProvider : IIdentityVerificationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http;
    private readonly MobileIdConfig _config;
    private readonly SmartIdConfig _smartIdConfig; // shares RelyingPartyUuid/Name
    private readonly ILogger<MobileIdProvider> _logger;
    private readonly byte[] _hmacKey;

    public MobileIdProvider(
        HttpClient http,
        IOptions<MobileIdConfig> config,
        IOptions<SmartIdConfig> smartIdConfig,
        ILogger<MobileIdProvider> logger)
    {
        _http = http;
        _config = config.Value;
        _smartIdConfig = smartIdConfig.Value;
        _logger = logger;

        // Prefer MobileId:HmacSecret; fall back to SmartId:HmacSecret; then dev placeholder.
        var secret = !string.IsNullOrWhiteSpace(_config.HmacSecret)
            ? _config.HmacSecret
            : !string.IsNullOrWhiteSpace(_smartIdConfig.HmacSecret)
                ? _smartIdConfig.HmacSecret
                : "ruumly-dev-identity-hmac-secret-change-me-0001";
        _hmacKey = Encoding.UTF8.GetBytes(secret);

        _http.BaseAddress = new Uri(_config.BaseUrl.TrimEnd('/') + '/');
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public string ProviderName => "mobile-id";

    // Mobile-ID shares the same SK account/credentials as Smart-ID.
    public Task<bool> IsConfiguredAsync() => Task.FromResult(_smartIdConfig.IsConfigured);

    /// <inheritdoc/>
    public async Task<IdentitySession> StartAuthenticationAsync(
        string personalCode,
        string country,
        string? phoneNumber = null,
        CancellationToken ct = default)
    {
        if (!_smartIdConfig.IsConfigured)
        {
            throw new InvalidOperationException(
                "Mobile-ID is not configured. Set SmartId:RelyingPartyUuid.");
        }

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new ArgumentException(
                "phoneNumber is required for Mobile-ID authentication.", nameof(phoneNumber));
        }

        // Generate 64 random bytes; SHA-256 auth hash.
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        var authHash = SHA256.HashData(randomBytes);
        var hashBase64 = Convert.ToBase64String(authHash);

        // 4-digit verification code.
        var vcInt = ((authHash[0] & 0xFF) << 8 | (authHash[1] & 0xFF)) % 10000;
        var verificationCode = vcInt.ToString("D4");

        // Normalise phone: ensure it starts with +
        var phone = phoneNumber.Replace(" ", "").Trim();
        if (!phone.StartsWith("+")) phone = "+" + phone;

        var requestBody = new
        {
            relyingPartyUUID = _smartIdConfig.RelyingPartyUuid,
            relyingPartyName = _smartIdConfig.RelyingPartyName,
            phoneNumber = phone,
            nationalIdentityNumber = personalCode,
            hash = hashBase64,
            hashType = "SHA256",
            language = "ENG",
        };

        using var response = await _http.PostAsJsonAsync("authentication", requestBody, JsonOptions, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Mobile-ID returned {StatusCode} starting session for country={Country}: {Body}",
                (int)response.StatusCode, country, body);
            throw new InvalidOperationException(
                $"Mobile-ID session start failed with HTTP {(int)response.StatusCode}.");
        }

        var doc = JsonDocument.Parse(body);
        var sessionId = doc.RootElement.GetProperty("sessionID").GetString()
            ?? throw new InvalidOperationException("Mobile-ID start response contained no sessionID.");

        _logger.LogInformation(
            "Mobile-ID session {SessionId} started for PNO{Country} (vc={VerificationCode}).",
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
        var url = $"authentication/session/{Uri.EscapeDataString(sessionId)}?timeoutMs=10000";

        try
        {
            using var response = await _http.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Mobile-ID returned {StatusCode} polling session {SessionId}: {Body}",
                    (int)response.StatusCode, sessionId, body);
                return new IdentityVerificationResult(
                    IdentityVerificationStatus.Failed,
                    FailureReason: $"HTTP {(int)response.StatusCode} from Mobile-ID");
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
                return new IdentityVerificationResult(
                    IdentityVerificationStatus.Failed,
                    FailureReason: $"Unexpected Mobile-ID state: {state}");
            }

            var endResult = root.TryGetProperty("result", out var resultEl) &&
                            resultEl.TryGetProperty("endResult", out var er)
                ? er.GetString()?.ToUpperInvariant()
                : null;

            if (endResult != "OK")
            {
                var (status, reason) = MapEndResult(endResult);
                _logger.LogInformation(
                    "Mobile-ID session {SessionId} ended with endResult '{EndResult}'.",
                    sessionId, endResult);
                return new IdentityVerificationResult(status, FailureReason: reason);
            }

            if (!root.TryGetProperty("cert", out var certEl) ||
                !certEl.TryGetProperty("value", out var certValue))
            {
                return new IdentityVerificationResult(
                    IdentityVerificationStatus.Failed,
                    FailureReason: "Certificate missing from Mobile-ID response");
            }

            var certBytes = Convert.FromBase64String(certValue.GetString() ?? "");
            return ParseCertificate(certBytes, sessionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Exception polling Mobile-ID session {SessionId}.", sessionId);
            return new IdentityVerificationResult(
                IdentityVerificationStatus.Failed,
                FailureReason: "Network error polling Mobile-ID");
        }
    }

    private IdentityVerificationResult ParseCertificate(byte[] certBytes, string sessionId)
    {
        try
        {
            using var cert = new X509Certificate2(certBytes);

            var verifiedName = GetSubjectComponent(cert, "CN");
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
                "Mobile-ID session {SessionId} verified: name='{Name}', country={Country}.",
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
            _logger.LogWarning(ex, "Failed to parse Mobile-ID certificate for session {SessionId}.", sessionId);
            return new IdentityVerificationResult(
                IdentityVerificationStatus.Failed,
                FailureReason: "Certificate parsing failed");
        }
    }

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
            "TIMEOUT" => (IdentityVerificationStatus.Expired, "Mobile-ID session timed out"),
            "USER_REFUSED" => (IdentityVerificationStatus.Failed, "User refused in the Mobile-ID app"),
            "PHONE_ABSENT" => (IdentityVerificationStatus.Failed, "User's phone was not reachable"),
            "DELIVERY_ERROR" => (IdentityVerificationStatus.Failed, "Mobile-ID message could not be delivered"),
            "SIM_ERROR" => (IdentityVerificationStatus.Failed, "SIM application error"),
            _ => (IdentityVerificationStatus.Failed, $"Mobile-ID end result: {endResult ?? "unknown"}"),
        };
}
