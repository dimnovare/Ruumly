using Ruumly.Backend.DTOs.Responses;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// The one thing that happens when the PUBLIC partner-application form is
/// submitted with an address that already has a Ruumly account: a short note to
/// the account holder saying an application arrived, that nothing was created,
/// and that they can submit it themselves after signing in.
///
/// The anonymous submitter proved nothing — not ownership of the mailbox, not
/// ownership of the account — so this email is deliberately built out of
/// TRANSLATED COPY AND A CONFIGURED URL ONLY. Nothing the form carried (company
/// name, registry code, notes) reaches it. A stranger who can make Ruumly mail
/// a victim must not also be able to choose what that mail says; the submitted
/// details go to the ops inbox instead, where a human reads them.
///
/// Same plain-text + inline-styled HTML shape as
/// <see cref="SupplierClaimComposer"/> — no images, no external CSS, no
/// tracking pixel.
/// </summary>
public static class SupplierApplySignInComposer
{
    // Brand palette, kept in sync with SupplierClaimComposer.
    private const string Teal      = "#00897B";
    private const string BodyText  = "#455A64";
    private const string MutedText = "#78909C";
    private const string PageBg    = "#f5f5f5";

    /// <summary>
    /// The localized sign-in page. Never a bare "/et/login": a relative path in
    /// an email body is a dead link.
    /// </summary>
    public static string SignInUrl(string? appUrl, string? language) =>
        FrontendUrl.Localized(
            string.IsNullOrWhiteSpace(appUrl) ? FrontendUrl.DefaultOrigin : appUrl,
            language,
            "login");

    public static SupplierIntroMessage Compose(
        string language,
        string signInUrl,
        string? opsInbox = null)
    {
        var t     = EmailTranslations.For(language);
        var inbox = string.IsNullOrWhiteSpace(opsInbox) ? OpsInbox.Fallback : opsInbox.Trim();

        var copy = new ApplyCopy(
            Greeting:  t.ApplySignInGreeting,
            Body:      t.ApplySignInBody,
            Cta:       t.ApplySignInCta,
            Url:       signInUrl,
            Ignore:    t.ApplySignInIgnore(inbox),
            Signature: t.IntroSignature);

        return new(language, t.ApplySignInSubject, BuildText(copy), BuildHtml(copy));
    }

    private sealed record ApplyCopy(
        string Greeting,
        string Body,
        string Cta,
        string Url,
        string Ignore,
        string Signature);

    private static string BuildText(ApplyCopy c) => string.Join("\n",
    [
        c.Greeting,
        "",
        c.Body,
        "",
        c.Url,
        "",
        c.Ignore,
        "",
        c.Signature,
    ]);

    private static string BuildHtml(ApplyCopy c)
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
