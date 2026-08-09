using Ruumly.Backend.DTOs.Responses;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// The magic-link email for the public "claim your profile" flow. It is the
/// only place the claim token ever appears, and it is sent to exactly one
/// address: the <c>ContactEmail</c> already stored on the supplier row — never
/// to whatever a visitor typed, even though the two are equal (ignoring case)
/// whenever this email is composed at all.
///
/// Deliberately shorter than the introduction mail: the recipient asked for it
/// seconds ago and needs one button. Same plain-text + inline-styled HTML shape
/// as <see cref="SupplierIntroComposer"/> — no images, no external CSS, no
/// tracking pixel.
/// </summary>
public static class SupplierClaimComposer
{
    // Brand palette, kept in sync with SupplierIntroComposer.
    private const string Teal      = "#00897B";
    private const string BodyText  = "#455A64";
    private const string MutedText = "#78909C";
    private const string PageBg    = "#f5f5f5";

    /// <summary>
    /// Claim URL for a supplier: /{lang}/claim/{slug}?token={token}. The token
    /// rides as a query parameter so the route itself stays the shareable,
    /// human-readable page the campaign advertises.
    /// </summary>
    public static string ClaimUrl(string? appUrl, string language, string slug, string token) =>
        FrontendUrl.Localized(appUrl, language, $"claim/{slug}?token={Uri.EscapeDataString(token)}");

    public static SupplierIntroMessage Compose(
        string language,
        string companyName,
        string claimUrl,
        int expiryHours,
        string? opsInbox = null)
    {
        var t       = EmailTranslations.For(language);
        var company = string.IsNullOrWhiteSpace(companyName) ? "" : companyName.Trim();
        var inbox   = string.IsNullOrWhiteSpace(opsInbox) ? OpsInbox.Fallback : opsInbox.Trim();

        var copy = new ClaimCopy(
            Greeting:  t.ClaimGreeting,
            Body:      t.ClaimBody(company),
            Cta:       t.ClaimCta,
            Url:       claimUrl,
            Expiry:    t.ClaimExpiry(expiryHours),
            Ignore:    t.ClaimIgnore(inbox),
            Signature: t.IntroSignature);

        return new(language, t.ClaimSubject, BuildText(copy), BuildHtml(copy));
    }

    private sealed record ClaimCopy(
        string Greeting,
        string Body,
        string Cta,
        string Url,
        string Expiry,
        string Ignore,
        string Signature);

    private static string BuildText(ClaimCopy c) => string.Join("\n",
    [
        c.Greeting,
        "",
        c.Body,
        "",
        c.Url,
        "",
        c.Expiry,
        "",
        c.Ignore,
        "",
        c.Signature,
    ]);

    private static string BuildHtml(ClaimCopy c)
    {
        // Same minimal escaper as the other composers: WebUtility.HtmlEncode
        // would turn every õ/ä/ü/ų and every Cyrillic character into a numeric
        // entity. Escaping & < > " covers text nodes and double-quoted
        // attributes, which is all this template emits.
        static string E(string value) => value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
        static string Multiline(string value) => E(value).Replace("\n", "<br>");

        return $"""
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
                        <p style="margin:0 0 12px;color:{BodyText};font-size:16px;">{E(c.Greeting)}</p>
                        <p style="margin:0 0 20px;color:{BodyText};font-size:15px;line-height:1.6;">{E(c.Body)}</p>
                        <table cellpadding="0" cellspacing="0" style="margin:0 0 20px;">
                          <tr><td style="background:{Teal};border-radius:6px;">
                            <a href="{E(c.Url)}" style="display:inline-block;padding:14px 28px;color:#ffffff;font-size:16px;font-weight:600;text-decoration:none;">{E(c.Cta)}</a>
                          </td></tr>
                        </table>
                        <p style="margin:0 0 20px;color:{MutedText};font-size:13px;word-break:break-all;">{E(c.Url)}</p>
                        <p style="margin:0 0 12px;color:{MutedText};font-size:14px;line-height:1.6;">{E(c.Expiry)}</p>
                        <p style="margin:0 0 24px;color:{MutedText};font-size:14px;line-height:1.6;">{E(c.Ignore)}</p>
                        <p style="margin:0;color:{MutedText};font-size:14px;line-height:1.6;">{Multiline(c.Signature)}</p>
                      </td>
                    </tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }
}
