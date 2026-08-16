using Ruumly.Backend.DTOs.Responses;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// The confirmation a provider gets the moment they redeem the claim magic link.
///
/// Until 2026-08-16 a successful claim sent exactly one email, and it went to
/// OPS. The provider proved control of their inbox, landed on an edit form,
/// corrected their details — and heard nothing from Ruumly at all. One of them
/// went looking for the contact form afterwards and sent a message whose entire
/// body was the word "test", which is a fair way to ask whether anyone is home.
///
/// The line that actually earns this mail is <c>ClaimDoneRequests</c>. Customer
/// requests are fanned out to <c>Supplier.ContactEmail</c>, so the mailbox this
/// message arrives in IS the channel the whole directory rests on. A provider
/// who has never been told that has no reason to keep the address current, and
/// no reason to look at it — and a cold request landing there months later reads
/// like spam from a company they have no memory of.
///
/// Deliberately NOT a receipt for the edits. It is sent at redemption, which is
/// the moment the profile counts as claimed; whether they go on to change
/// anything is a separate question and this mail does not pretend to know.
///
/// Same plain-text + inline-styled HTML shape as
/// <see cref="SupplierClaimComposer"/>, whose mail it directly follows: no
/// images, no external CSS, no tracking pixel.
/// </summary>
public static class ClaimConfirmedComposer
{
    // Brand palette, kept in sync with the other composers.
    private const string Teal      = "#00897B";
    private const string BodyText  = "#455A64";
    private const string MutedText = "#78909C";
    private const string PageBg    = "#f5f5f5";

    /// <summary>
    /// The provider's own public page: /{lang}/partner/{slug}. Falls back to the
    /// public origin when AppUrl is unset — a bare "/et/partner/x" in an email
    /// body is a dead link, and this one is the mail's only button.
    /// </summary>
    public static string ProfileUrl(string? appUrl, string language, string slug) =>
        FrontendUrl.Localized(
            string.IsNullOrWhiteSpace(appUrl) ? FrontendUrl.DefaultOrigin : appUrl,
            language, $"partner/{slug}");

    public static SupplierIntroMessage Compose(
        string language,
        string companyName,
        string profileUrl,
        string? contactUrl = null)
    {
        var t       = EmailTranslations.For(language);
        var company = string.IsNullOrWhiteSpace(companyName) ? "" : companyName.Trim();

        var copy = new DoneCopy(
            Greeting:  t.ClaimGreeting,
            Body:      t.ClaimDoneBody,
            Cta:       t.ClaimDoneCta,
            Url:       profileUrl,
            Requests:  t.ClaimDoneRequests,
            Edit:      t.ClaimDoneEdit,
            Questions: string.IsNullOrWhiteSpace(contactUrl) ? null : t.OutreachQuestions(contactUrl),
            Signature: t.IntroSignature);

        return new(language, t.ClaimDoneSubject(company), BuildText(copy), BuildHtml(copy));
    }

    private sealed record DoneCopy(
        string Greeting,
        string Body,
        string Cta,
        string Url,
        string Requests,
        string Edit,
        string? Questions,
        string Signature);

    private static string BuildText(DoneCopy c)
    {
        var lines = new List<string>
        {
            c.Greeting, "", c.Body, "", $"{c.Cta}: {c.Url}", "", c.Requests, "", c.Edit,
        };

        if (c.Questions is not null)
        {
            lines.Add("");
            lines.Add(c.Questions);
        }

        lines.Add("");
        lines.Add(c.Signature);

        return string.Join("\n", lines);
    }

    private static string BuildHtml(DoneCopy c)
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

        var questionsHtml = c.Questions is null
            ? ""
            : $"""
                        <p style="margin:0 0 24px;color:{MutedText};font-size:14px;line-height:1.6;">{E(c.Questions)}</p>
               """;

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
                        <p style="margin:0 0 20px;color:{BodyText};font-size:15px;line-height:1.6;">{E(c.Requests)}</p>
                        <p style="margin:0 0 20px;color:{BodyText};font-size:15px;line-height:1.6;">{E(c.Edit)}</p>
            {questionsHtml}
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
