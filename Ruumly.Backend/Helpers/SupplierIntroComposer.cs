using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// The one-off supplier INTRODUCTION email. Ruumly's directory was populated
/// from public research: hundreds of providers who have never heard of us. When
/// an auto-fanout availability request (<see cref="ProviderOutreachComposer"/>)
/// lands in that inbox cold, it reads like spam. This email goes out FIRST.
///
/// Structure rewritten 2026-08 to the founder's own draft, which is organised as
/// three answered questions rather than a feature list: what we want from you,
/// why answering matters, and what your profile is. The single behaviour it
/// exists to change is stated outright — a provider who replies with a link to
/// their own website cannot be put in front of the customer, because the
/// customer came to Ruumly precisely to avoid visiting ten websites.
///
/// Plain text plus a simple inline-styled HTML body: no images, no tracking
/// pixel, no external CSS. A tracking pixel in a first-contact cold email is
/// exactly the thing that makes it look like the spam we are trying not to be.
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

    // Brand palette, kept in sync with ProviderOutreachComposer.
    private const string Teal      = "#00897B";
    private const string Heading   = "#263238";
    private const string BodyText  = "#455A64";
    private const string MutedText = "#78909C";
    private const string PageBg    = "#f5f5f5";
    private const string HairLine  = "#E0E0E0";

    /// <summary>
    /// Language of the intro mail — the supplier's own country, exactly as
    /// provider outreach picks it (EE→et, LV→lv, LT→lt, anything else→en).
    /// Delegated rather than duplicated so the two can never drift apart.
    /// </summary>
    public static string LanguageFor(Supplier supplier) =>
        ProviderOutreachComposer.LanguageFor(supplier);

    /// <summary>
    /// The claim destination for this supplier: /{lang}/claim/{slug}, the real
    /// verification flow (prove control of the stored contact email, then edit
    /// your own row).
    ///
    /// The guard mirrors the claim endpoint's own eligibility EXACTLY (slug +
    /// published + directory row): a cold email must never contain a link that
    /// 404s, so if the API would refuse the claim the mail falls back to the
    /// plain mailto instead.
    /// </summary>
    public static string? ClaimPageUrl(Supplier supplier, string? appUrl, string language) =>
        string.IsNullOrWhiteSpace(supplier.Slug)
        || !supplier.IsPartnerPagePublished
        || !supplier.IsDirectoryListing
        || string.IsNullOrWhiteSpace(appUrl)
            ? null
            : FrontendUrl.Localized(appUrl, language, $"claim/{supplier.Slug}");

    public static SupplierIntroMessage Compose(
        Supplier supplier,
        string? appUrl = null,
        string? opsInbox = null)
    {
        var language = LanguageFor(supplier);
        return ComposeInLanguage(
            language,
            supplier.Name,
            ClaimPageUrl(supplier, appUrl, language),
            appUrl,
            opsInbox);
    }

    internal static SupplierIntroMessage ComposeInLanguage(
        string language,
        string companyName,
        string? claimPageUrl,
        string? appUrl = null,
        string? opsInbox = null)
    {
        var t       = EmailTranslations.For(language);
        var company = string.IsNullOrWhiteSpace(companyName) ? "" : companyName.Trim();
        var inbox   = string.IsNullOrWhiteSpace(opsInbox) ? OpsInbox.Fallback : opsInbox.Trim();
        // Reply or the contact page — the only two channels offered since the
        // support phone was retired (2026-08). Falls back to the public origin
        // when no AppUrl is configured, so the line can never go out relative.
        var contact = FrontendUrl.Contact(appUrl, language);

        var copy = new IntroCopy(
            Greeting:        t.IntroGreeting,
            Opening:         t.IntroOpening,
            WhoWeAre:        t.IntroWhoWeAre,
            Forwarding:      t.IntroForwarding,
            NotTestRequests: t.IntroNotTestRequests,
            ExpectHeading:   t.IntroExpectHeading,
            ExpectIntro:     t.IntroExpectIntro,
            ExpectBullets:   [t.IntroExpectBullet1, t.IntroExpectBullet2, t.IntroExpectBullet3],
            NoAccount:       t.IntroNoAccount,
            IfNotSuitable:   t.IntroIfNotSuitable,
            WhyHeading:      t.IntroWhyHeading,
            WhyBody:         t.IntroWhyBody,
            Goal:            t.IntroGoal,
            Volume:          t.IntroVolume,
            ProfileHeading:  t.IntroProfileHeading,
            ProfileBody:     t.IntroProfileListed(company),
            ClaimIntro:      claimPageUrl is null ? null : t.IntroClaimIntro,
            ClaimCta:        t.IntroClaimCta,
            ClaimUrl:        claimPageUrl,
            ClaimByEmail:    claimPageUrl is null ? t.IntroClaimByEmail(inbox) : null,
            ClaimMailto:     MailTo(inbox, ClaimKeyword, company),
            PriceList:       t.IntroPriceList,
            VisibilityLater: t.IntroVisibilityLater,
            FinalAsk:        t.IntroFinalAsk,
            Questions:       t.IntroQuestions(contact),
            ContactUrl:      contact,
            OptOut:          t.IntroOptOut(OptOutKeyword),
            OptOutLabel:     t.IntroOptOutLinkLabel,
            OptOutMailto:    MailTo(inbox, OptOutKeyword, company),
            Signature:       t.IntroSignature);

        return new(language, t.IntroSubject(company), BuildText(copy), BuildHtml(copy));
    }

    /// <summary>Everything both bodies render, resolved once.</summary>
    private sealed record IntroCopy(
        string Greeting,
        string Opening,
        string WhoWeAre,
        string Forwarding,
        string NotTestRequests,
        string ExpectHeading,
        string ExpectIntro,
        IReadOnlyList<string> ExpectBullets,
        string NoAccount,
        string IfNotSuitable,
        string WhyHeading,
        string WhyBody,
        string Goal,
        string Volume,
        string ProfileHeading,
        string ProfileBody,
        string? ClaimIntro,
        string ClaimCta,
        string? ClaimUrl,
        string? ClaimByEmail,
        string ClaimMailto,
        string PriceList,
        string VisibilityLater,
        string FinalAsk,
        string Questions,
        string ContactUrl,
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
            c.Opening,
            "",
            c.WhoWeAre,
            "",
            c.Forwarding,
            "",
            c.NotTestRequests,
            "",
            c.ExpectHeading,
            "",
            c.ExpectIntro,
        };

        // "- " rather than a bullet glyph: a plain-text client renders it the
        // same everywhere, and it survives a reply-quote intact.
        foreach (var bullet in c.ExpectBullets)
            lines.Add($"- {bullet}");

        lines.Add("");
        lines.Add(c.NoAccount);
        lines.Add("");
        lines.Add(c.IfNotSuitable);
        lines.Add("");
        lines.Add(c.WhyHeading);
        lines.Add("");
        lines.Add(c.WhyBody);
        lines.Add("");
        lines.Add(c.Goal);
        lines.Add("");
        lines.Add(c.Volume);
        lines.Add("");
        lines.Add(c.ProfileHeading);
        lines.Add("");
        lines.Add(c.ProfileBody);
        lines.Add("");

        // The claim page when the supplier is eligible for one, a plain email
        // address when it is not. Never a raw mailto: URL in the text body — an
        // operator reading in a plain-text client should see an address, not
        // markup.
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
        lines.Add(c.PriceList);
        lines.Add("");
        lines.Add(c.VisibilityLater);
        lines.Add("");
        lines.Add(c.FinalAsk);
        lines.Add("");
        lines.Add(c.Questions);
        lines.Add("");
        lines.Add(c.OptOut);
        lines.Add("");
        lines.Add(c.Signature);

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Minimal inline-styled HTML: branded header, the founder's three sections
    /// with real headings, one CTA button, the opt-out as a real one-click
    /// mailto link. No images, no external CSS, no tracking pixel.
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

        // A bare URL in an HTML body is not auto-linked by most clients, and the
        // contact page is one of only two channels this email offers. Anchor the
        // URL inside the sentence.
        static string Linkified(string sentence, string url)
        {
            var at = sentence.IndexOf(url, StringComparison.Ordinal);
            if (at < 0) return E(sentence);
            return E(sentence[..at])
                 + $"""<a href="{E(url)}" style="color:{Teal};">{E(url)}</a>"""
                 + E(sentence[(at + url.Length)..]);
        }

        string P(string text, string margin = "0 0 16px") =>
            $"""<p style="margin:{margin};color:{BodyText};font-size:15px;line-height:1.6;">{E(text)}</p>""";

        string H(string text) =>
            $"""<p style="margin:26px 0 12px;padding-top:18px;border-top:1px solid {HairLine};color:{Heading};font-size:17px;font-weight:700;line-height:1.4;">{E(text)}</p>""";

        var bullets = string.Concat(c.ExpectBullets.Select(b =>
            $"""<li style="margin:0 0 8px;color:{BodyText};font-size:15px;line-height:1.6;">{E(b)}</li>"""));

        var questionsHtml =
            $"""<p style="margin:0 0 22px;color:{BodyText};font-size:15px;line-height:1.6;">{Linkified(c.Questions, c.ContactUrl)}</p>""";

        var claimHtml = c.ClaimUrl is not null
            ? $"""
               {P(c.ClaimIntro!, "0 0 14px")}
               <table cellpadding="0" cellspacing="0" style="margin:0 0 10px;">
                 <tr><td style="background:{Teal};border-radius:6px;">
                   <a href="{E(c.ClaimUrl)}" style="display:inline-block;padding:13px 26px;color:#ffffff;font-size:15px;font-weight:600;text-decoration:none;">{E(c.ClaimCta)}</a>
                 </td></tr>
               </table>
               <p style="margin:0 0 16px;color:{MutedText};font-size:13px;word-break:break-all;">{E(c.ClaimUrl)}</p>
               """
            : $"""
               <p style="margin:0 0 16px;color:{BodyText};font-size:15px;line-height:1.6;">
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
                        <p style="margin:0 0 14px;color:{BodyText};font-size:16px;">{E(c.Greeting)}</p>
                        {P(c.Opening)}
                        {P(c.WhoWeAre)}
                        {P(c.Forwarding)}
                        {P(c.NotTestRequests)}
                        {H(c.ExpectHeading)}
                        {P(c.ExpectIntro, "0 0 10px")}
                        <ul style="margin:0 0 16px;padding-left:22px;">{bullets}</ul>
                        {P(c.NoAccount)}
                        {P(c.IfNotSuitable)}
                        {H(c.WhyHeading)}
                        {P(c.WhyBody)}
                        {P(c.Goal)}
                        {P(c.Volume)}
                        {H(c.ProfileHeading)}
                        {P(c.ProfileBody)}
                        {claimHtml}
                        {P(c.PriceList)}
                        {P(c.VisibilityLater)}
                        <p style="margin:22px 0 16px;padding:14px 16px;background:#E0F2F1;border-radius:6px;color:{Heading};font-size:15px;line-height:1.6;font-weight:600;">{Multiline(c.FinalAsk)}</p>
                        {questionsHtml}
                        <p style="margin:0 0 22px;color:{MutedText};font-size:14px;line-height:1.6;">
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
