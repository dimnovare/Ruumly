using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// The one-off supplier INTRODUCTION email. Ruumly's directory was populated
/// from public research: ~562 providers who have never heard of us. When an
/// auto-fanout availability request (<see cref="ProviderOutreachComposer"/>)
/// lands in that inbox cold, it reads like spam. This email goes out FIRST and
/// says four things and nothing else: who we are, that you are already listed
/// for free, that real customer requests may arrive, and how to get off the
/// list.
///
/// Deliberately short — a small operator reads it on a phone, and past ~200
/// words it will not be read at all. Same shape as the outreach mail: plain
/// text plus a simple inline-styled HTML body, no images, no tracking pixel,
/// no external CSS.
/// </summary>
public static class SupplierIntroComposer
{
    /// <summary>
    /// Opt-out token. Identical in every language on purpose: one inbox filter
    /// on "REMOVE" has to catch a Lithuanian operator's reply too. Legally
    /// required for B2B marketing mail in the EU (ePrivacy), so it is never
    /// conditional and never a form.
    /// </summary>
    public const string OptOutKeyword = "REMOVE";

    /// <summary>Subject token for the claim mailto — same filtering rationale.</summary>
    public const string ClaimKeyword = "CLAIM";

    /// <summary>
    /// Support number printed in the "questions?" line. The campaign promises a
    /// human on the other end, so unlike the outreach mail this line is never
    /// omitted — PlatformSettings <c>opsPhone</c> overrides it when set.
    /// </summary>
    public const string DefaultOpsPhone = "+372 5805 7795";

    // Brand palette, kept in sync with ProviderOutreachComposer.
    private const string Teal      = "#00897B";
    private const string BodyText  = "#455A64";
    private const string MutedText = "#78909C";
    private const string PageBg    = "#f5f5f5";

    /// <summary>
    /// Language of the intro mail — the supplier's own country, exactly as
    /// provider outreach picks it (EE→et, LV→lv, LT→lt, anything else→en).
    /// Delegated rather than duplicated so the two can never drift apart.
    /// </summary>
    public static string LanguageFor(Supplier supplier) =>
        ProviderOutreachComposer.LanguageFor(supplier);

    /// <summary>
    /// The claim destination for this supplier: its own public partner page,
    /// which already carries the "claim this profile" CTA
    /// (estonia-space-hub PartnerPage.tsx). Null when the supplier has no
    /// published page — the caller then falls back to a plain mailto, because
    /// a cold email must never contain a link that 404s.
    /// </summary>
    public static string? PartnerPageUrl(Supplier supplier, string? appUrl, string language) =>
        string.IsNullOrWhiteSpace(supplier.Slug)
        || !supplier.IsPartnerPagePublished
        || string.IsNullOrWhiteSpace(appUrl)
            ? null
            : FrontendUrl.Localized(appUrl, language, $"partner/{supplier.Slug}");

    public static SupplierIntroMessage Compose(
        Supplier supplier,
        string? appUrl = null,
        string? opsInbox = null,
        string? opsPhone = null)
    {
        var language = LanguageFor(supplier);
        return ComposeInLanguage(
            language,
            supplier.Name,
            PartnerPageUrl(supplier, appUrl, language),
            opsInbox,
            opsPhone);
    }

    internal static SupplierIntroMessage ComposeInLanguage(
        string language,
        string companyName,
        string? partnerPageUrl,
        string? opsInbox = null,
        string? opsPhone = null)
    {
        var t       = EmailTranslations.For(language);
        var company = string.IsNullOrWhiteSpace(companyName) ? "" : companyName.Trim();
        var inbox   = string.IsNullOrWhiteSpace(opsInbox) ? OpsInbox.Fallback : opsInbox.Trim();
        var phone   = string.IsNullOrWhiteSpace(opsPhone) ? DefaultOpsPhone : opsPhone.Trim();

        var copy = new IntroCopy(
            Greeting:      t.IntroGreeting,
            WhoWeAre:      t.IntroWhoWeAre,
            Listed:        t.IntroListed(company),
            WhatToExpect:  t.IntroWhatToExpect,
            Questions:     t.IntroQuestions(phone),
            ClaimIntro:    partnerPageUrl is null ? null : t.IntroClaimIntro,
            ClaimCta:      t.IntroClaimCta,
            ClaimUrl:      partnerPageUrl,
            ClaimByEmail:  partnerPageUrl is null ? t.IntroClaimByEmail(inbox) : null,
            ClaimMailto:   MailTo(inbox, ClaimKeyword, company),
            OptOut:        t.IntroOptOut(OptOutKeyword),
            OptOutLabel:   t.IntroOptOutLinkLabel,
            OptOutMailto:  MailTo(inbox, OptOutKeyword, company),
            Signature:     t.IntroSignature);

        return new(language, t.IntroSubject, BuildText(copy), BuildHtml(copy));
    }

    /// <summary>Everything both bodies render, resolved once.</summary>
    private sealed record IntroCopy(
        string Greeting,
        string WhoWeAre,
        string Listed,
        string WhatToExpect,
        string Questions,
        string? ClaimIntro,
        string ClaimCta,
        string? ClaimUrl,
        string? ClaimByEmail,
        string ClaimMailto,
        string OptOut,
        string OptOutLabel,
        string OptOutMailto,
        string Signature);

    /// <summary>
    /// One-click opt-out/claim address. The subject carries a stable English
    /// keyword plus the company name, so a single filter rule catches every
    /// reply and the founder sees which business it is without opening it.
    /// </summary>
    private static string MailTo(string inbox, string keyword, string company)
    {
        var subject = string.IsNullOrEmpty(company) ? keyword : $"{keyword} {company}";
        return $"mailto:{inbox}?subject={Uri.EscapeDataString(subject)}";
    }

    private static string BuildText(IntroCopy c)
    {
        var lines = new List<string>
        {
            c.Greeting,
            "",
            c.WhoWeAre,
            "",
            c.Listed,
            "",
            c.WhatToExpect,
            "",
            c.Questions,
            "",
        };

        // Claim: the partner page when it exists, a plain email address when it
        // does not. Never a raw mailto: URL in the text body — an operator
        // reading in a plain-text client should see an address, not markup.
        if (c.ClaimUrl is not null)
        {
            lines.Add(c.ClaimIntro!);
            lines.Add(c.ClaimUrl);
        }
        else
        {
            lines.Add(c.ClaimByEmail!);
        }

        lines.Add("");
        lines.Add(c.OptOut);
        lines.Add("");
        lines.Add(c.Signature);

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Minimal inline-styled HTML mirroring the outreach template: branded
    /// header, five short paragraphs, one CTA button, the opt-out as a real
    /// one-click mailto link. No images, no external CSS, no tracking pixel —
    /// a tracking pixel in a first-contact cold email is exactly the thing that
    /// makes it look like the spam we are trying not to be.
    /// </summary>
    private static string BuildHtml(IntroCopy c)
    {
        // Same minimal escaper as ProviderOutreachComposer: WebUtility.HtmlEncode
        // would turn every õ/ä/ü/ų and every Cyrillic character into a numeric
        // entity. Escaping & < > " covers text nodes and double-quoted
        // attributes, which is all this template emits.
        static string E(string value) => value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
        static string Multiline(string value) => E(value).Replace("\n", "<br>");

        var claimHtml = c.ClaimUrl is not null
            ? $"""
                         <p style="margin:0 0 16px;color:{BodyText};font-size:15px;line-height:1.6;">{E(c.ClaimIntro!)}</p>
                         <table cellpadding="0" cellspacing="0" style="margin:0 0 20px;">
                           <tr><td style="background:{Teal};border-radius:6px;">
                             <a href="{E(c.ClaimUrl)}" style="display:inline-block;padding:14px 28px;color:#ffffff;font-size:16px;font-weight:600;text-decoration:none;">{E(c.ClaimCta)}</a>
                           </td></tr>
                         </table>
                         <p style="margin:0 0 20px;color:{MutedText};font-size:13px;word-break:break-all;">{E(c.ClaimUrl)}</p>
                   """
            : $"""
                         <p style="margin:0 0 20px;color:{BodyText};font-size:15px;line-height:1.6;">
                           <a href="{E(c.ClaimMailto)}" style="color:{Teal};">{E(c.ClaimByEmail!)}</a>
                         </p>
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
                        <p style="margin:0 0 16px;color:{BodyText};font-size:15px;line-height:1.6;">{E(c.WhoWeAre)}</p>
                        <p style="margin:0 0 16px;color:{BodyText};font-size:15px;line-height:1.6;">{E(c.Listed)}</p>
                        <p style="margin:0 0 16px;color:{BodyText};font-size:15px;line-height:1.6;">{E(c.WhatToExpect)}</p>
                        <p style="margin:0 0 20px;color:{BodyText};font-size:15px;line-height:1.6;">{E(c.Questions)}</p>
            {claimHtml}
                        <p style="margin:0 0 24px;color:{MutedText};font-size:14px;line-height:1.6;">
                          {E(c.OptOut)}<br>
                          <a href="{E(c.OptOutMailto)}" style="color:{Teal};">{E(c.OptOutLabel)}</a>
                        </p>
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
