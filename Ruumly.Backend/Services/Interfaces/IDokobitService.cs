namespace Ruumly.Backend.Services.Interfaces;

/// <summary>
/// Wrapper around the Dokobit <b>Documents Gateway</b> (the real qualified
/// e-signature API). Auth is a single <c>access_token</c> query param on every
/// request; requests are <c>application/x-www-form-urlencoded</c> with bracket
/// field names; responses are snake_case JSON parsed defensively.
///
/// Env-gated: with no access token configured <see cref="IsEnabled"/> is false and
/// every method throws <see cref="InvalidOperationException"/> — callers gate on it.
/// </summary>
public interface IDokobitService
{
    /// <summary>True when Signing:Dokobit:AccessToken is configured.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Uploads PDF bytes to the gateway via <c>POST /api/file/upload.json</c> as
    /// <c>file[name]</c> / <c>file[digest]</c> (SHA-256 hex) / <c>file[content]</c>
    /// (base64) — the file is never hosted publicly. Returns the file token used to
    /// create the signing.
    /// </summary>
    Task<DokobitUploadResult> UploadDocumentAsync(string fileName, byte[] pdfBytes, CancellationToken ct = default);

    /// <summary>
    /// Creates a single-signer PDF signing via <c>POST /api/signing/create.json</c>
    /// for the uploaded file token. Offers Smart-ID and Mobile-ID; sets the
    /// postback URL. Returns the signing token and the ready-to-redirect signing URL.
    /// </summary>
    Task<DokobitSigningResult> CreateSigningRequestAsync(
        string fileToken,
        string documentName,
        DokobitSigner signer,
        string postbackUrl,
        CancellationToken ct = default);

    /// <summary>
    /// Fetches signing status via <c>GET /api/signing/{token}/status.json</c>,
    /// mapping the provider status to <see cref="DokobitSigningStatus"/> and parsing
    /// the verified signer identity (personal code, signing option, name) when present.
    /// </summary>
    Task<DokobitStatusResult> GetStatusAsync(string signingToken, CancellationToken ct = default);

    /// <summary>
    /// Resolves the signed PDF bytes once a signing is complete — inline base64,
    /// a download URL, or <c>GET /api/signing/{token}/files.json</c> as fallbacks.
    /// Returns null when no signed file is available yet.
    /// </summary>
    Task<byte[]?> DownloadSignedDocumentAsync(string signingToken, CancellationToken ct = default);
}

/// <summary>Signer details passed to create-signing. Name is split into name+surname internally.</summary>
public record DokobitSigner(
    string FullName,
    string? PersonalCode,
    string CountryCode,
    string? Phone,
    string? Email);

public record DokobitUploadResult(string Token, bool Success, string? Error);

public record DokobitSigningResult(string SigningToken, string SigningUrl, bool Success, string? Error);

/// <summary>
/// Result of a status poll: the mapped status plus whatever verified identity the
/// gateway has attached (populated on Completed). <see cref="VerifiedIdCode"/> is the
/// gateway-verified personal code — the authoritative identity, not a form value.
/// </summary>
public record DokobitStatusResult(
    DokobitSigningStatus Status,
    string? VerifiedIdCode,
    string? VerifiedName,
    string? SigningOption);

public enum DokobitSigningStatus
{
    Pending,
    Completed,
    Cancelled,
    Error,
}
