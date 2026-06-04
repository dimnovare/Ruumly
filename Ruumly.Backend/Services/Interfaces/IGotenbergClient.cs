namespace Ruumly.Backend.Services.Interfaces;

/// <summary>
/// Renders a filled <c>.docx</c> to PDF via a Gotenberg sidecar
/// (<c>POST {Gotenberg:Url}/forms/libreoffice/convert</c>). No LibreOffice runs in the
/// API process. Env-gated: <see cref="IsEnabled"/> is false when <c>Gotenberg:Url</c> is
/// unset, and rendering throws a clear "not configured" error.
/// </summary>
public interface IGotenbergClient
{
    /// <summary>True when Gotenberg:Url is configured.</summary>
    bool IsEnabled { get; }

    /// <summary>Converts the given docx bytes to PDF bytes. Throws when not configured or on failure.</summary>
    Task<byte[]> ConvertDocxToPdfAsync(byte[] docxBytes, string fileName, CancellationToken ct = default);
}
