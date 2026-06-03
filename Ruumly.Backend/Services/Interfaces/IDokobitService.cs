namespace Ruumly.Backend.Services.Interfaces;

public interface IDokobitService
{
    /// <summary>True when DOKOBIT_API_TOKEN is configured in the environment.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Uploads a document (HTML or PDF bytes) to Dokobit Documents Gateway.
    /// Returns the document token needed to create a signing request.
    /// </summary>
    Task<DokobitUploadResult> UploadDocumentAsync(string fileName, byte[] fileBytes, CancellationToken ct = default);

    /// <summary>
    /// Creates a Dokobit signing request for the uploaded document.
    /// Returns the signing token and the URL to redirect the signer to.
    /// </summary>
    Task<DokobitSigningResult> CreateSigningRequestAsync(
        string documentToken,
        string signerName,
        string signerIdCode,
        string signerEmail,
        string returnUrl,
        CancellationToken ct = default);

    /// <summary>Polls the status of an in-progress signing request.</summary>
    Task<DokobitSigningStatus> GetStatusAsync(string signingToken, CancellationToken ct = default);

    /// <summary>
    /// Downloads the signed document bytes once status == Completed.
    /// </summary>
    Task<byte[]> DownloadSignedDocumentAsync(string signingToken, CancellationToken ct = default);
}

public record DokobitUploadResult(string Token, bool Success, string? Error);
public record DokobitSigningResult(string SigningToken, string SigningUrl, bool Success, string? Error);

public enum DokobitSigningStatus
{
    Pending,
    Completed,
    Cancelled,
    Error,
}
