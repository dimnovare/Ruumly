using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

/// <summary>
/// Thin wrapper around the Dokobit Documents Gateway REST API.
///
/// Configuration (via environment variables / Railway):
///   DOKOBIT__APITOKEN   — Dokobit API access token (required to enable)
///   DOKOBIT__BASEURL    — Base URL, defaults to https://gateway-sandbox.dokobit.com
///                         For production: https://gateway.dokobit.com
///
/// When DOKOBIT__APITOKEN is absent, IsEnabled == false and all methods throw
/// InvalidOperationException — callers must gate on IsEnabled before calling.
///
/// API design assumptions (from Dokobit Gateway API documentation):
///   POST /api/upload
///     Request: multipart/form-data with field "file" = file bytes
///     Response: { "status": "ok", "token": "..." }
///
///   POST /api/signing/create
///     Request JSON: {
///       "doc":     { "token": "..." },
///       "signers": [{ "id": "signer1", "name": "...", "personalCode": "...", "email": "...", "type": "signature" }],
///       "settings": { "returnUrl": "..." }
///     }
///     Response: { "status": "ok", "token": "...", "signingUrl": "..." }
///
///   GET /api/signing/{token}/status
///     Response: { "status": "pending"|"completed"|"cancelled", ... }
///
///   GET /api/signing/{token}/download
///     Response: raw file bytes (PDF)
///
/// All requests require ?access_token={apiToken} query parameter.
/// </summary>
public class DokobitService : IDokobitService
{
    private readonly HttpClient _http;
    private readonly string?    _apiToken;
    private readonly string     _baseUrl;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public DokobitService(HttpClient http, IConfiguration configuration)
    {
        _http     = http;
        _apiToken = configuration["Dokobit:ApiToken"];
        _baseUrl  = (configuration["Dokobit:BaseUrl"] ?? "https://gateway-sandbox.dokobit.com")
                    .TrimEnd('/');
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_apiToken);

    // ─── Upload ──────────────────────────────────────────────────────────────

    public async Task<DokobitUploadResult> UploadDocumentAsync(
        string fileName, byte[] fileBytes, CancellationToken ct = default)
    {
        EnsureEnabled();

        using var form    = new MultipartFormDataContent();
        var       content = new ByteArrayContent(fileBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(
            fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                ? "application/pdf"
                : "text/html");
        form.Add(content, "file", fileName);

        var url = $"{_baseUrl}/api/upload?access_token={Uri.EscapeDataString(_apiToken!)}";
        try
        {
            var resp = await _http.PostAsync(url, form, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return new DokobitUploadResult("", false,
                    $"HTTP {(int)resp.StatusCode}: {body}");

            var result = JsonSerializer.Deserialize<DokobitUploadResponse>(body, _jsonOptions);
            if (result?.Status != "ok" || string.IsNullOrEmpty(result.Token))
                return new DokobitUploadResult("", false,
                    $"Dokobit upload failed: {result?.Message ?? body}");

            return new DokobitUploadResult(result.Token, true, null);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            return new DokobitUploadResult("", false, ex.Message);
        }
    }

    // ─── Create signing request ───────────────────────────────────────────────

    public async Task<DokobitSigningResult> CreateSigningRequestAsync(
        string documentToken,
        string signerName,
        string signerIdCode,
        string signerEmail,
        string returnUrl,
        CancellationToken ct = default)
    {
        EnsureEnabled();

        var payload = new
        {
            doc = new { token = documentToken },
            signers = new[]
            {
                new
                {
                    id           = "signer1",
                    name         = signerName,
                    personalCode = signerIdCode,
                    email        = signerEmail,
                    type         = "signature",
                }
            },
            settings = new { returnUrl },
        };

        var json    = JsonSerializer.Serialize(payload, _jsonOptions);
        var reqBody = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var url = $"{_baseUrl}/api/signing/create?access_token={Uri.EscapeDataString(_apiToken!)}";
        try
        {
            var resp = await _http.PostAsync(url, reqBody, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return new DokobitSigningResult("", "", false,
                    $"HTTP {(int)resp.StatusCode}: {body}");

            var result = JsonSerializer.Deserialize<DokobitCreateSigningResponse>(body, _jsonOptions);
            if (result?.Status != "ok" || string.IsNullOrEmpty(result.Token))
                return new DokobitSigningResult("", "", false,
                    $"Dokobit signing/create failed: {result?.Message ?? body}");

            return new DokobitSigningResult(result.Token, result.SigningUrl ?? "", true, null);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            return new DokobitSigningResult("", "", false, ex.Message);
        }
    }

    // ─── Status ───────────────────────────────────────────────────────────────

    public async Task<DokobitSigningStatus> GetStatusAsync(
        string signingToken, CancellationToken ct = default)
    {
        EnsureEnabled();

        var url = $"{_baseUrl}/api/signing/{Uri.EscapeDataString(signingToken)}/status" +
                  $"?access_token={Uri.EscapeDataString(_apiToken!)}";
        try
        {
            var resp = await _http.GetAsync(url, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) return DokobitSigningStatus.Error;

            var result = JsonSerializer.Deserialize<DokobitStatusResponse>(body, _jsonOptions);
            return result?.Status switch
            {
                "completed" => DokobitSigningStatus.Completed,
                "cancelled"  => DokobitSigningStatus.Cancelled,
                "pending"    => DokobitSigningStatus.Pending,
                _            => DokobitSigningStatus.Error,
            };
        }
        catch
        {
            return DokobitSigningStatus.Error;
        }
    }

    // ─── Download signed document ─────────────────────────────────────────────

    public async Task<byte[]> DownloadSignedDocumentAsync(
        string signingToken, CancellationToken ct = default)
    {
        EnsureEnabled();

        var url = $"{_baseUrl}/api/signing/{Uri.EscapeDataString(signingToken)}/download" +
                  $"?access_token={Uri.EscapeDataString(_apiToken!)}";

        var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void EnsureEnabled()
    {
        if (!IsEnabled)
            throw new InvalidOperationException(
                "Dokobit is not configured. Set DOKOBIT__APITOKEN environment variable.");
    }

    // ─── Private response DTOs ────────────────────────────────────────────────

    private sealed class DokobitUploadResponse
    {
        public string? Status  { get; set; }
        public string? Token   { get; set; }
        public string? Message { get; set; }
    }

    private sealed class DokobitCreateSigningResponse
    {
        public string? Status     { get; set; }
        public string? Token      { get; set; }
        [JsonPropertyName("signingUrl")]
        public string? SigningUrl { get; set; }
        public string? Message    { get; set; }
    }

    private sealed class DokobitStatusResponse
    {
        public string? Status { get; set; }
        // Dokobit may return signer info once completed — reserved for future use.
        public List<DokobitSignerInfo>? Signers { get; set; }
    }

    private sealed class DokobitSignerInfo
    {
        public string? Name         { get; set; }
        public string? PersonalCode { get; set; }
    }
}
