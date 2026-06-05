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

    /// <summary>
    /// Builds a minimal valid <c>.docx</c> from a sequence of paragraph strings — one
    /// <c>Paragraph</c> per input string. <c>{{token}}</c> placeholders are written verbatim so a
    /// subsequent <see cref="Fill"/> replaces them. Embedded newlines become line breaks within the
    /// paragraph. Used to build the in-code platform-default contract when a supplier has no
    /// uploaded template.
    /// </summary>
    byte[] BuildDocx(IEnumerable<string> paragraphs);

    /// <summary>
    /// Appends a plain-text clause as a new paragraph at the end of the document body.
    /// Used to always inject the sign-then-pay "conditional on payment" clause regardless
    /// of whether the provider's template includes the corresponding token — a signed-but-unpaid
    /// contract must state it binds no one. The clause is appended verbatim (no token substitution).
    /// </summary>
    byte[] AppendClause(byte[] docxBytes, string clauseText);
}
