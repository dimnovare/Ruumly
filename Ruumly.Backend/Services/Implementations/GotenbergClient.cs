using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

/// <summary>
/// <see cref="IGotenbergClient"/> backed by a Gotenberg sidecar service. Posts the docx
/// as multipart to <c>{Gotenberg:Url}/forms/libreoffice/convert</c> and returns the PDF
/// bytes. Gotenberg is a trusted internal service (a Railway sidecar), so its URL is
/// allowed to be private; only the scheme is required to be HTTP(S).
/// </summary>
public sealed class GotenbergClient : IGotenbergClient
{
    private readonly HttpClient _http;
    private readonly ILogger<GotenbergClient> _logger;
    private readonly string? _baseUrl;

    public GotenbergClient(HttpClient http, IConfiguration configuration, ILogger<GotenbergClient> logger)
    {
        _http    = http;
        _logger  = logger;
        _baseUrl = configuration["Gotenberg:Url"]?.TrimEnd('/');
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_baseUrl);

    public async Task<byte[]> ConvertDocxToPdfAsync(byte[] docxBytes, string fileName, CancellationToken ct = default)
    {
        if (!IsEnabled)
            throw new InvalidOperationException(
                "Gotenberg is not configured. Set Gotenberg:Url (GOTENBERG__URL).");

        var url = $"{_baseUrl}/forms/libreoffice/convert";

        // Trusted internal sidecar — require only an HTTP(S) scheme.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException($"Gotenberg:Url is not a valid HTTP(S) URL: {_baseUrl}");

        var safeName = string.IsNullOrWhiteSpace(fileName) ? "contract.docx" : fileName;
        if (!safeName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            safeName += ".docx";

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(docxBytes);
        fileContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue(
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        // Gotenberg keys conversion off the uploaded file's name/extension.
        form.Add(fileContent, "files", safeName);

        using var resp = await _http.PostAsync(url, form, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Gotenberg convert {File} → HTTP {Code}: {Body}",
                safeName, (int)resp.StatusCode, body);
            throw new InvalidOperationException(
                $"Gotenberg docx→PDF conversion failed with HTTP {(int)resp.StatusCode}.");
        }

        return await resp.Content.ReadAsByteArrayAsync(ct);
    }
}
