namespace Ruumly.Backend.Helpers;

/// <summary>
/// The house look of a Ruumly email: palette, and the outer shell every message
/// is poured into — grey page, white card, teal bar with the wordmark.
///
/// These five values are currently copy-pasted into six composers
/// (ProviderOutreachComposer, CustomerRequestAckComposer, SupplierIntroComposer,
/// SupplierClaimComposer, SupplierApplySignInComposer and AuthService). They are
/// declared here so that NEW code has one place to point at rather than a
/// seventh copy; consolidating the existing six is a separate change, because
/// those templates are live transactional mail and rewriting them is not worth
/// doing in passing.
/// </summary>
internal static class EmailBrand
{
    internal const string Teal      = "#00897B";
    internal const string BodyText  = "#455A64";
    internal const string MutedText = "#78909C";
    internal const string PageBg    = "#f5f5f5";
    internal const string HairLine  = "#ECEFF1";

    /// <summary>
    /// Escapes a text node or a double-quoted attribute value.
    ///
    /// Deliberately NOT WebUtility.HtmlEncode: that turns every non-ASCII
    /// character into a numeric entity, which would mangle õ/ä/ü/ų and Cyrillic
    /// into unreadable soup in a UTF-8 mail. Escaping &amp; &lt; &gt; " covers
    /// everything these templates produce.
    /// </summary>
    internal static string E(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");

    /// <summary>
    /// Wraps already-rendered body HTML in the card. Table-based and
    /// inline-styled on purpose: Outlook ignores &lt;style&gt; blocks and most of
    /// flexbox, so a "modern" layout would arrive as a single unstyled column for
    /// a meaningful share of recipients.
    /// </summary>
    internal static string Page(string innerHtml) => $"""
        <!DOCTYPE html>
        <html>
        <head>
          <meta charset="UTF-8">
          <meta name="viewport" content="width=device-width,initial-scale=1.0">
        </head>
        <body style="margin:0;padding:0;background:{PageBg};font-family:Arial,Helvetica,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background:{PageBg};padding:24px 0;">
            <tr><td align="center">
              <table width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:#ffffff;border-radius:8px;">
                <tr>
                  <td style="background:{Teal};padding:20px 28px;border-radius:8px 8px 0 0;color:#ffffff;font-size:20px;font-weight:700;">Ruumly</td>
                </tr>
                <tr>
                  <td style="padding:28px;">
        {innerHtml}
                  </td>
                </tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;
}
