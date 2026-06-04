using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

/// <summary>
/// <see cref="IContractDocumentService"/> backed by the Open XML SDK.
///
/// <para>
/// Templates use <c>{{token}}</c> placeholders. Word frequently splits a single logical
/// placeholder across several <see cref="Run"/>/<see cref="Text"/> elements (spell-check
/// ranges, revision marks), so both discovery and replacement first reassemble each
/// paragraph's full text before matching/substituting. Headers and footers are covered too.
/// </para>
///
/// <para>PDF rendering is delegated to Gotenberg (<see cref="IGotenbergClient"/>); this
/// service never spawns a process.</para>
/// </summary>
public sealed class OpenXmlContractDocumentService : IContractDocumentService
{
    // {{ token }} — letters, digits and underscores, optional surrounding whitespace.
    private static readonly Regex PlaceholderRegex = new(
        @"\{\{\s*([A-Za-z0-9_]+)\s*\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <inheritdoc />
    public IReadOnlyList<string> DiscoverTokens(byte[] docxBytes)
    {
        var tokens = new List<string>();
        var seen   = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            using var stream = new MemoryStream();
            stream.Write(docxBytes, 0, docxBytes.Length);
            stream.Position = 0;

            using var doc = WordprocessingDocument.Open(stream, isEditable: false);
            if (doc.MainDocumentPart is null) return tokens;

            foreach (var paragraph in EnumerateAllParagraphs(doc))
            {
                var combined = GetParagraphText(paragraph);
                if (combined.Length == 0) continue;

                foreach (Match match in PlaceholderRegex.Matches(combined))
                {
                    var name = match.Groups[1].Value;
                    if (seen.Add(name)) tokens.Add(name);
                }
            }
        }
        catch
        {
            // Not a valid docx — surface "no tokens" rather than throwing here; the
            // upload endpoint validates the file is a real docx separately.
            return tokens;
        }

        return tokens;
    }

    /// <inheritdoc />
    public byte[] Fill(byte[] docxBytes, IReadOnlyDictionary<string, string> values)
    {
        using var stream = new MemoryStream();
        stream.Write(docxBytes, 0, docxBytes.Length);
        stream.Position = 0;

        using (var doc = WordprocessingDocument.Open(stream, isEditable: true))
        {
            foreach (var paragraph in EnumerateAllParagraphs(doc))
                ReplaceInParagraph(paragraph, values);

            doc.MainDocumentPart?.Document?.Save();
            foreach (var header in doc.MainDocumentPart?.HeaderParts ?? Enumerable.Empty<HeaderPart>())
                header.Header?.Save();
            foreach (var footer in doc.MainDocumentPart?.FooterParts ?? Enumerable.Empty<FooterPart>())
                footer.Footer?.Save();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Reassembles the paragraph's text, replaces tokens on the combined string, writes
    /// the result into the first <see cref="Text"/> node and blanks the rest. Run/paragraph
    /// properties are untouched, so the first run's formatting governs the substituted value.
    /// </summary>
    private static void ReplaceInParagraph(Paragraph paragraph, IReadOnlyDictionary<string, string> values)
    {
        var textNodes = paragraph.Descendants<Text>().ToList();
        if (textNodes.Count == 0) return;

        var combined = string.Concat(textNodes.Select(t => t.Text));
        if (!PlaceholderRegex.IsMatch(combined)) return;

        var replaced = FillPlaceholders(combined, values);

        var first  = textNodes[0];
        first.Text  = replaced;
        first.Space = SpaceProcessingModeValues.Preserve;

        for (var i = 1; i < textNodes.Count; i++)
            textNodes[i].Text = string.Empty;
    }

    /// <summary>
    /// Pure substitution: every <c>{{token}}</c> → <c>values[token]</c>; an absent or null
    /// value becomes the empty string. Whitespace inside the braces is tolerated; other
    /// text is left exactly as-is. Exposed for unit tests.
    /// </summary>
    internal static string FillPlaceholders(string text, IReadOnlyDictionary<string, string> values) =>
        PlaceholderRegex.Replace(
            text,
            m => values.TryGetValue(m.Groups[1].Value, out var value) ? value ?? "" : "");

    private static string GetParagraphText(Paragraph paragraph)
    {
        var builder = new StringBuilder();
        foreach (var text in paragraph.Descendants<Text>())
            builder.Append(text.Text);
        return builder.ToString();
    }

    /// <summary>All paragraphs across the body plus every header and footer part.</summary>
    private static IEnumerable<Paragraph> EnumerateAllParagraphs(WordprocessingDocument doc)
    {
        var main = doc.MainDocumentPart;
        if (main is null) yield break;

        if (main.Document is { } document)
            foreach (var paragraph in document.Descendants<Paragraph>())
                yield return paragraph;

        foreach (var header in main.HeaderParts)
            if (header.Header is not null)
                foreach (var paragraph in header.Header.Descendants<Paragraph>())
                    yield return paragraph;

        foreach (var footer in main.FooterParts)
            if (footer.Footer is not null)
                foreach (var paragraph in footer.Footer.Descendants<Paragraph>())
                    yield return paragraph;
    }
}
