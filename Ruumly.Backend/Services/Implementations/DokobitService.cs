using System.Security.Cryptography;
using System.Text.Json;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

/// <summary>
/// Dokobit <b>Documents Gateway</b> client (real qualified e-signature API).
///
/// <para>
/// Wire format — empirically confirmed against gateway-sandbox.dokobit.com (2026-06-04):
/// every request is <c>application/x-www-form-urlencoded</c> with bracket field names
/// and carries <c>?access_token=</c>; every response is snake_case JSON. Endpoints end
/// in <c>.json</c>.
/// </para>
///
/// <list type="number">
///   <item><b>Upload</b> — <c>POST /api/file/upload.json</c> with <c>file[name]</c>,
///   <c>file[digest]</c> (SHA-256 hex lowercase) and <c>file[content]</c> (base64).
///   The sandbox returned <c>{ "status":"ok", "token":"…" }</c> and the file was
///   immediately <c>uploaded</c> (no poll needed), but a short status poll is kept
///   defensively.</item>
///   <item><b>Create signing</b> — <c>POST /api/signing/create.json</c> with
///   <c>type=pdf</c>, <c>name</c>, <c>signers[0][id|name|surname|code|country_code]</c>,
///   <c>signers[0][signing_purpose]=signature</c> (REQUIRED — the sandbox 400s without
///   it), <c>signers[0][signing_options][]=smartid|mobile</c>, <c>files[0][token]</c>
///   and <c>postback_url</c>. Returns <c>{ "status":"ok", "token":"…",
///   "signers":{ "1":"signerAccessToken" } }</c>.</item>
///   <item><b>Signing URL</b> (built locally): <c>{base}/signing/{signingToken}?access_token={signerAccessToken}</c>.</item>
///   <item><b>Status</b> — <c>GET /api/signing/{token}/status.json</c>. The sandbox
///   returns <c>{ "status":"pending", "signers":{ "1":{ "status":"pending" } } }</c>.
///   Once signed the signer object carries the verified identity (<c>code</c>,
///   <c>signing_option</c>, …) and the signed file is referenced by <c>file</c>/<c>files</c>.
///   Both the nested-signer shape and a flat top-level <c>signer_info</c> are parsed.</item>
/// </list>
///
/// Env-gated: with <c>Signing:Dokobit:AccessToken</c> absent <see cref="IsEnabled"/> is
/// false and every method throws a clear "not configured" error.
/// </summary>
public class DokobitService : IDokobitService
{
    private const string SandboxBaseUrl    = "https://gateway-sandbox.dokobit.com";
    private const string ProductionBaseUrl = "https://gateway.dokobit.com";

    // The upload is normally "uploaded" immediately; this is a defensive bound.
    private const int MaxUploadStatusAttempts = 8;
    private static readonly TimeSpan UploadStatusDelay = TimeSpan.FromMilliseconds(500);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly ILogger<DokobitService> _logger;
    private readonly string? _accessToken;
    private readonly string  _baseUrl;

    public DokobitService(HttpClient http, IConfiguration configuration, ILogger<DokobitService> logger)
    {
        _http        = http;
        _logger      = logger;
        _accessToken = configuration["Signing:Dokobit:AccessToken"];

        var environment = configuration["Signing:Dokobit:Environment"] ?? "test";
        _baseUrl = string.Equals(environment, "production", StringComparison.OrdinalIgnoreCase)
            ? ProductionBaseUrl
            : SandboxBaseUrl;
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_accessToken);

    // ─── Upload ──────────────────────────────────────────────────────────────

    public async Task<DokobitUploadResult> UploadDocumentAsync(
        string fileName, byte[] pdfBytes, CancellationToken ct = default)
    {
        EnsureEnabled();

        try
        {
            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("file[name]",    fileName),
                new KeyValuePair<string, string>("file[digest]",  Sha256Hex(pdfBytes)),
                new KeyValuePair<string, string>("file[content]", Convert.ToBase64String(pdfBytes)),
            });

            using var resp = await _http.PostAsync(BuildUrl("api/file/upload.json"), form, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Dokobit upload {File} → HTTP {Code}: {Body}", fileName, (int)resp.StatusCode, body);
                return new DokobitUploadResult("", false, $"HTTP {(int)resp.StatusCode}: {body}");
            }

            var parsed = JsonSerializer.Deserialize<UploadResponse>(body, JsonOptions);
            if (parsed?.Status != "ok" || string.IsNullOrWhiteSpace(parsed.Token))
                return new DokobitUploadResult("", false, $"Dokobit upload failed: {parsed?.Message ?? body}");

            // Best-effort: confirm the gateway finished storing the file. Non-fatal.
            await WaitForUploadAsync(parsed.Token!, fileName, ct);

            return new DokobitUploadResult(parsed.Token!, true, null);
        }
        catch (Exception ex) when (ex is not InvalidOperationException && ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Dokobit upload failed for {File}", fileName);
            return new DokobitUploadResult("", false, ex.Message);
        }
    }

    /// <summary>
    /// Best-effort poll of <c>GET /api/file/upload/status/{token}.json</c> until the
    /// status is <c>uploaded</c>. The sandbox returns <c>uploaded</c> on the first call,
    /// so this normally returns immediately; it never throws (the create call will
    /// surface a genuine problem).
    /// </summary>
    private async Task WaitForUploadAsync(string fileToken, string fileName, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxUploadStatusAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var resp = await _http.GetAsync(
                    BuildUrl($"api/file/upload/status/{Uri.EscapeDataString(fileToken)}.json"), ct);
                if (resp.IsSuccessStatusCode)
                {
                    var body   = await resp.Content.ReadAsStringAsync(ct);
                    var status = JsonSerializer.Deserialize<UploadStatusResponse>(body, JsonOptions)?.Status
                        ?.Trim().ToLowerInvariant();
                    if (status is "uploaded" or "ok")
                        return;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Dokobit upload-status poll failed for {File} (non-fatal).", fileName);
                return; // poll is advisory; don't block the flow
            }

            if (attempt < MaxUploadStatusAttempts)
                await Task.Delay(UploadStatusDelay, ct);
        }
    }

    // ─── Create signing ────────────────────────────────────────────────────────

    public async Task<DokobitSigningResult> CreateSigningRequestAsync(
        string fileToken,
        string documentName,
        DokobitSigner signer,
        string postbackUrl,
        CancellationToken ct = default)
    {
        EnsureEnabled();

        try
        {
            var (name, surname) = SplitName(signer.FullName);

            var form = new List<KeyValuePair<string, string>>
            {
                new("type",                            "pdf"),
                new("name",                            documentName),
                new("signers[0][id]",                  "1"),
                new("signers[0][name]",                name),
                new("signers[0][surname]",             surname),
                // REQUIRED — the sandbox rejects create-signing without it (code 10000).
                new("signers[0][signing_purpose]",     "signature"),
                // Offer the two eID methods Ruumly supports in the Baltics.
                new("signers[0][signing_options][]",   "smartid"),
                new("signers[0][signing_options][]",   "mobile"),
                new("files[0][token]",                 fileToken),
                new("postback_url",                    postbackUrl),
            };

            if (!string.IsNullOrWhiteSpace(signer.CountryCode))
                form.Add(new("signers[0][country_code]", signer.CountryCode.Trim()));
            if (!string.IsNullOrWhiteSpace(signer.PersonalCode))
                form.Add(new("signers[0][code]", signer.PersonalCode!.Trim()));
            if (!string.IsNullOrWhiteSpace(signer.Phone))
                form.Add(new("signers[0][phone]", signer.Phone!.Trim()));

            using var resp = await _http.PostAsync(
                BuildUrl("api/signing/create.json"), new FormUrlEncodedContent(form), ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Dokobit create-signing {Name} → HTTP {Code}: {Body}",
                    documentName, (int)resp.StatusCode, body);
                return new DokobitSigningResult("", "", false, $"HTTP {(int)resp.StatusCode}: {body}");
            }

            var parsed = JsonSerializer.Deserialize<CreateSigningResponse>(body, JsonOptions);
            if (parsed?.Status != "ok" || string.IsNullOrWhiteSpace(parsed.Token))
                return new DokobitSigningResult("", "", false, $"Dokobit signing/create failed: {parsed?.Message ?? body}");

            // signers map is { signerId -> signerAccessToken }. We used id "1".
            string? signerAccessToken = null;
            if (parsed.Signers is { Count: > 0 } signers)
                signerAccessToken = signers.TryGetValue("1", out var byId) ? byId : signers.Values.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(signerAccessToken))
            {
                _logger.LogWarning("Dokobit create-signing {Token} returned no signer access token: {Body}",
                    parsed.Token, body);
                return new DokobitSigningResult(parsed.Token!, "", false, "Dokobit returned no signer access token.");
            }

            var signingUrl =
                $"{_baseUrl}/signing/{Uri.EscapeDataString(parsed.Token!)}?access_token={Uri.EscapeDataString(signerAccessToken!)}";

            return new DokobitSigningResult(parsed.Token!, signingUrl, true, null);
        }
        catch (Exception ex) when (ex is not InvalidOperationException && ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Dokobit create-signing failed for {Name}", documentName);
            return new DokobitSigningResult("", "", false, ex.Message);
        }
    }

    // ─── Status ──────────────────────────────────────────────────────────────

    public async Task<DokobitStatusResult> GetStatusAsync(string signingToken, CancellationToken ct = default)
    {
        EnsureEnabled();

        try
        {
            using var resp = await _http.GetAsync(
                BuildUrl($"api/signing/{Uri.EscapeDataString(signingToken)}/status.json"), ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Dokobit status {Token} → HTTP {Code}: {Body}",
                    signingToken, (int)resp.StatusCode, body);
                return new DokobitStatusResult(DokobitSigningStatus.Error, null, null, null);
            }

            return ParseStatus(body);
        }
        catch (Exception ex) when (ex is not InvalidOperationException && ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Dokobit status read failed for {Token}", signingToken);
            return new DokobitStatusResult(DokobitSigningStatus.Error, null, null, null);
        }
    }

    /// <summary>
    /// Parses a status.json body into a normalized status + verified identity. Handles
    /// both the sandbox shape (per-signer object nested under <c>signers["1"]</c>) and a
    /// flat top-level <c>signer_info</c>. Exposed internally so the parse is unit-testable
    /// without a network call.
    /// </summary>
    internal static DokobitStatusResult ParseStatus(string json)
    {
        var parsed = JsonSerializer.Deserialize<SigningStatusResponse>(json, JsonOptions);

        // Prefer the per-signer object (sandbox shape); fall back to flat signer_info.
        var signer = parsed?.Signers?.Values.FirstOrDefault() ?? parsed?.SignerInfo;

        // The overall signing status, falling back to the individual signer's status.
        var statusStr = parsed?.Status ?? signer?.Status;
        var status    = MapStatus(statusStr, signer?.Status);

        return new DokobitStatusResult(
            status,
            string.IsNullOrWhiteSpace(signer?.Code)          ? null : signer!.Code,
            BuildName(signer),
            string.IsNullOrWhiteSpace(signer?.SigningOption) ? null : signer!.SigningOption);
    }

    /// <summary>
    /// Maps a Dokobit status to <see cref="DokobitSigningStatus"/>. An unknown/ambiguous
    /// status is treated as <see cref="DokobitSigningStatus.Pending"/> so a real signature
    /// is never flipped to a terminal error by an unenumerated value.
    /// </summary>
    internal static DokobitSigningStatus MapStatus(string? overall, string? signerStatus)
    {
        var s = (overall ?? signerStatus)?.Trim().ToLowerInvariant();
        return s switch
        {
            "signed" or "completed" or "complete" or "archived" or "ok" => DokobitSigningStatus.Completed,
            "pending" or "waiting" or "started"                          => DokobitSigningStatus.Pending,
            "declined" or "rejected" or "cancelled" or "canceled"        => DokobitSigningStatus.Cancelled,
            "expired" or "failed" or "error"                             => DokobitSigningStatus.Error,
            _                                                             => DokobitSigningStatus.Pending,
        };
    }

    private static string? BuildName(SignerInfo? signer)
    {
        if (signer is null) return null;
        var full = string.Join(" ",
            new[] { signer.Name, signer.Surname }.Where(p => !string.IsNullOrWhiteSpace(p)));
        return string.IsNullOrWhiteSpace(full) ? null : full;
    }

    // ─── Download signed PDF ────────────────────────────────────────────────

    public async Task<byte[]?> DownloadSignedDocumentAsync(string signingToken, CancellationToken ct = default)
    {
        EnsureEnabled();

        try
        {
            // 1) status.json may carry inline base64 or a file URL once signed.
            using (var statusResp = await _http.GetAsync(
                BuildUrl($"api/signing/{Uri.EscapeDataString(signingToken)}/status.json"), ct))
            {
                if (statusResp.IsSuccessStatusCode)
                {
                    var body  = await statusResp.Content.ReadAsStringAsync(ct);
                    var bytes = await ResolveSignedBytesAsync(body, signingToken, ct);
                    if (bytes is { Length: > 0 }) return bytes;
                }
            }

            // 2) Fallback: files.json lists the signed artifacts.
            using (var filesResp = await _http.GetAsync(
                BuildUrl($"api/signing/{Uri.EscapeDataString(signingToken)}/files.json"), ct))
            {
                if (filesResp.IsSuccessStatusCode)
                {
                    var body  = await filesResp.Content.ReadAsStringAsync(ct);
                    var bytes = await ResolveSignedBytesAsync(body, signingToken, ct);
                    if (bytes is { Length: > 0 }) return bytes;
                }
            }

            _logger.LogWarning("Dokobit signed file for {Token} could not be resolved.", signingToken);
            return null;
        }
        catch (Exception ex) when (ex is not InvalidOperationException && ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Dokobit signed-file download failed for {Token}", signingToken);
            return null;
        }
    }

    /// <summary>
    /// Resolves signed PDF bytes from a status/files JSON body: prefers inline base64
    /// (<c>file.content</c> / <c>files[0].content</c>), else downloads from a file URL
    /// (<c>file</c> string, <c>file.url</c>/<c>download_url</c>, or <c>files[0].*</c>).
    /// </summary>
    private async Task<byte[]?> ResolveSignedBytesAsync(string json, string signingToken, CancellationToken ct)
    {
        SignedFilesEnvelope? env;
        try { env = JsonSerializer.Deserialize<SignedFilesEnvelope>(json, JsonOptions); }
        catch { return null; }
        if (env is null) return null;

        var candidates = new List<SignedFile?> { env.File }.Concat(env.Files ?? new()).ToList();

        // Inline base64 first.
        foreach (var f in candidates)
        {
            if (!string.IsNullOrWhiteSpace(f?.Content))
            {
                try { return Convert.FromBase64String(f!.Content!.Trim()); }
                catch { /* not base64 — fall through to URL handling */ }
            }
        }

        // The top-level "file" may itself be a plain URL string.
        if (!string.IsNullOrWhiteSpace(env.FileUrlString))
        {
            var bytes = await TryDownloadFromUrlAsync(env.FileUrlString, signingToken, ct);
            if (bytes is { Length: > 0 }) return bytes;
        }

        // Otherwise download from any URL field on the file objects.
        foreach (var f in candidates)
        {
            var url = f?.Url ?? f?.DownloadUrl;
            if (string.IsNullOrWhiteSpace(url)) continue;
            var bytes = await TryDownloadFromUrlAsync(url, signingToken, ct);
            if (bytes is { Length: > 0 }) return bytes;
        }

        return null;
    }

    /// <summary>
    /// Downloads bytes from a Dokobit file URL (absolute or gateway-relative), appending
    /// the access token — the signed-file endpoint 403s without it.
    /// </summary>
    private async Task<byte[]?> TryDownloadFromUrlAsync(string url, string signingToken, CancellationToken ct)
    {
        var tokenParam = $"access_token={Uri.EscapeDataString(_accessToken!)}";
        string requestUrl;
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var sep    = url.Contains('?') ? '&' : '?';
            requestUrl = $"{url}{sep}{tokenParam}";
        }
        else
        {
            requestUrl = $"{_baseUrl}/{url.TrimStart('/')}?{tokenParam}";
        }

        using var resp = await _http.GetAsync(requestUrl, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Dokobit download {Token} from {Url} → HTTP {Code}",
                signingToken, url, (int)resp.StatusCode);
            return null;
        }
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void EnsureEnabled()
    {
        if (!IsEnabled)
            throw new InvalidOperationException(
                "Dokobit is not configured. Set Signing:Dokobit:AccessToken (SIGNING__DOKOBIT__ACCESSTOKEN).");
    }

    private string BuildUrl(string path) =>
        $"{_baseUrl}/{path}?access_token={Uri.EscapeDataString(_accessToken!)}";

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal static (string Name, string Surname) SplitName(string fullName)
    {
        var trimmed = (fullName ?? string.Empty).Trim();
        var lastSpace = trimmed.LastIndexOf(' ');
        return lastSpace < 0
            ? (trimmed, string.Empty)
            : (trimmed[..lastSpace].Trim(), trimmed[(lastSpace + 1)..].Trim());
    }

    // ─── Response shapes (parsed defensively; missing fields tolerated) ──────────

    private sealed record UploadResponse
    {
        public string? Status  { get; init; }
        public string? Token   { get; init; }
        public string? Message { get; init; }
    }

    private sealed record UploadStatusResponse
    {
        public string? Status { get; init; }
    }

    private sealed record CreateSigningResponse
    {
        public string? Status  { get; init; }
        public string? Token   { get; init; }
        public string? Message { get; init; }
        public Dictionary<string, string>? Signers { get; init; }
    }

    /// <summary>
    /// status.json — both shapes: the per-signer map <c>signers: { "1": { … } }</c>
    /// (sandbox) and a flat top-level <c>signer_info</c> (some docs/postbacks).
    /// </summary>
    private sealed record SigningStatusResponse
    {
        public string? Status { get; init; }
        public Dictionary<string, SignerInfo>? Signers { get; init; }
        public SignerInfo? SignerInfo { get; init; }
    }

    private sealed record SignerInfo
    {
        public string? Status        { get; init; }
        public string? Code          { get; init; }
        public string? Phone         { get; init; }
        public string? CountryCode   { get; init; }
        public string? SigningOption { get; init; }
        public string? SigningTime   { get; init; }
        public string? Type          { get; init; }
        public string? Name          { get; init; }
        public string? Surname       { get; init; }
    }

    /// <summary>
    /// Envelope for resolving the signed PDF from status.json / files.json. The <c>file</c>
    /// field is read raw because the gateway returns it either as an object
    /// (<c>{ content|url|download_url }</c>) or as a plain URL string — both handled.
    /// </summary>
    private sealed record SignedFilesEnvelope
    {
        public List<SignedFile>? Files { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("file")]
        public JsonElement? FileRaw { get; init; }

        /// <summary>The <c>file</c> field parsed as an object, or null when it isn't one.</summary>
        public SignedFile? File =>
            FileRaw is { ValueKind: JsonValueKind.Object } el
                ? el.Deserialize<SignedFile>(JsonOptions)
                : null;

        /// <summary>The <c>file</c> field when it is a plain URL string, else null.</summary>
        public string? FileUrlString =>
            FileRaw is { ValueKind: JsonValueKind.String } el ? el.GetString() : null;
    }

    private sealed record SignedFile
    {
        public string? Content     { get; init; }
        public string? Url         { get; init; }
        public string? DownloadUrl { get; init; }
        public string? Name        { get; init; }
        public string? Digest      { get; init; }
    }
}
