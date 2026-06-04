namespace Ruumly.Backend.Services.Interfaces;

/// <summary>
/// Reads and fills Word <c>.docx</c> contract templates that use <c>{{token}}</c>
/// placeholders. Run-merge aware (Word frequently splits a placeholder across several
/// runs) and covers the body plus headers and footers. PDF rendering is a separate
/// concern handled by <see cref="IGotenbergClient"/>.
/// </summary>
public interface IContractDocumentService
{
    /// <summary>
    /// Returns the distinct <c>{{token}}</c> names found in the docx, in first-seen
    /// order, reassembling tokens split across runs. Returns an empty list for input
    /// that isn't a valid docx.
    /// </summary>
    IReadOnlyList<string> DiscoverTokens(byte[] docxBytes);

    /// <summary>
    /// Returns new docx bytes with every <c>{{token}}</c> replaced by its value. A token
    /// with no entry (or a null value) becomes the empty string — the output never leaks
    /// an unresolved placeholder.
    /// </summary>
    byte[] Fill(byte[] docxBytes, IReadOnlyDictionary<string, string> values);
}
