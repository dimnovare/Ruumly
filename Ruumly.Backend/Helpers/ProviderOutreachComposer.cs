using Ruumly.Backend.Constants;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;

namespace Ruumly.Backend.Helpers;

public static class ProviderOutreachComposer
{
    /// <summary>
    /// A need date at most this many days away (or already past) is flagged as
    /// urgent — in the subject line and at the top of the body. Next-day demand
    /// is the dominant pattern in the concierge funnel, and a provider who only
    /// skims the subject must still see it.
    /// </summary>
    public const int UrgentWithinDays = 3;

    // Brand palette, kept in sync with the other transactional templates
    // (AuthService password reset): teal header, slate body text.
    private const string Teal      = "#00897B";
    private const string BodyText  = "#455A64";
    private const string MutedText = "#78909C";
    private const string PageBg    = "#f5f5f5";

    /// <summary>
    /// Composes the availability-request email for a provider, as HTML with a
    /// plain-text fallback. When <paramref name="quoteToken"/> is supplied, the
    /// primary CTA becomes "Submit your price → {https://ruumly.eu/{lang}/quote/{token}}"
    /// (the token is per-recipient); replying to the email with a price is offered
    /// as the explicit alternative, since Reply-To is the ops inbox. Never
    /// contains the customer's name/email/phone — the admin brokers the
    /// introduction.
    /// </summary>
    /// <param name="opsPhone">
    /// Support phone from PlatformSettings (<c>opsPhone</c>). When null/blank the
    /// phone line is omitted entirely — never render an empty or placeholder number.
    /// </param>
    public static ProviderOutreachMessage Compose(
        DemandLead lead,
        Supplier supplier,
        string? appUrl = null,
        string? quoteToken = null,
        string? opsPhone = null)
        => ComposeInLanguage(LanguageFor(supplier), lead, appUrl, quoteToken, opsPhone);

    /// <summary>
    /// Language of outbound provider mail — the supplier's own country, not the
    /// customer's. Today only et/lv/lt/en are reachable this way; the Russian
    /// strings exist for parity with the rest of the transactional catalogue.
    /// </summary>
    public static string LanguageFor(Supplier supplier) =>
        supplier.Country?.ToUpperInvariant() switch
        {
            "LV" => "lv",
            "LT" => "lt",
            "EE" => "et",
            _ => "en",
        };

    internal static ProviderOutreachMessage ComposeInLanguage(
        string language,
        DemandLead lead,
        string? appUrl = null,
        string? quoteToken = null,
        string? opsPhone = null)
    {
        var t = EmailTranslations.For(language);
        var route = string.IsNullOrWhiteSpace(lead.ToCity)
            ? lead.City
            : $"{lead.City} → {lead.ToCity}";
        var category = t.CategoryLabel(lead.Category);

        // "—" tells a provider nothing and reads as a broken template. An absent
        // date/details becomes a real instruction instead ("as soon as possible —
        // we'll confirm it" / "not specified — we'll check with the customer").
        var date     = lead.NeedDate?.ToString("yyyy-MM-dd");
        var dateText = date ?? t.OutreachDateAsap;
        var details  = string.IsNullOrWhiteSpace(lead.Details)
            ? t.OutreachDetailsMissing
            : lead.Details.Trim();

        var isUrgent   = lead.NeedDate is { } needDate
                      && needDate.Date <= DateTime.UtcNow.Date.AddDays(UrgentWithinDays);
        var urgentLine = isUrgent ? t.OutreachUrgent(date!) : null;

        var subject = t.OutreachSubject(category, route);
        if (isUrgent) subject = $"{t.OutreachUrgentBadge}: {subject}";

        var quoteUrl = string.IsNullOrWhiteSpace(quoteToken)
            ? null
            : FrontendUrl.Localized(appUrl, language, $"quote/{quoteToken}");
        var phone = string.IsNullOrWhiteSpace(opsPhone) ? null : opsPhone.Trim();

        var facts = new List<(string Label, string Value)>
        {
            (t.OutreachLabelService, category),
        };

        // Packing is sold as a line inside a mover's offer, never standalone, so a
        // "packing" request becomes a Moving lead and the ask survives only as a
        // marker in the Query machine summary (ServiceCategories.HasPackingAddOn
        // documents the whole contract). Recover it here and render it as a
        // LOCALIZED fact line: it must never travel as English prose inside
        // Details, which is printed verbatim into an email written in the
        // provider's own language. Placed straight after the service, because it
        // changes the scope of the job being priced.
        if (ServiceCategories.HasPackingAddOn(lead.Query))
            facts.Add((t.CategoryPacking, t.OutreachPackingAddOn));

        facts.Add((t.OutreachLabelLocation, route));
        facts.Add((t.OutreachLabelDate,     dateText));
        facts.Add((t.OutreachLabelDetails,  details));

        return new(
            language,
            subject,
            BuildText(t, facts, urgentLine, quoteUrl, phone),
            BuildHtml(t, facts, urgentLine, quoteUrl, phone));
    }

    private static string BuildText(
        EmailTranslations.EmailStrings t,
        IReadOnlyList<(string Label, string Value)> facts,
        string? urgentLine,
        string? quoteUrl,
        string? phone)
    {
        var lines = new List<string>
        {
            t.OutreachGreeting,
            "",
            t.OutreachIntro,
            "",
        };

        if (urgentLine is not null)
        {
            lines.Add($"** {urgentLine} **");
            lines.Add("");
        }

        foreach (var (label, value) in facts)
            lines.Add($"{label}: {value}");

        lines.Add("");
        lines.Add(t.OutreachAsk);

        if (quoteUrl is not null)
        {
            lines.Add("");
            lines.Add($"{t.OutreachQuoteCta} → {quoteUrl}");
        }

        lines.Add("");
        lines.Add(t.OutreachReplyAlternative);
        lines.Add("");
        lines.Add(t.OutreachSignature);
        if (phone is not null)
            lines.Add($"{t.OutreachPhoneLabel}: {phone}");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Minimal inline-styled HTML: no external CSS, no images, no tracking
    /// pixels — just a branded header, the request facts, one CTA and the
    /// signature. Every interpolated value is HTML-encoded: the details field is
    /// raw customer free text.
    /// </summary>
    private static string BuildHtml(
        EmailTranslations.EmailStrings t,
        IReadOnlyList<(string Label, string Value)> facts,
        string? urgentLine,
        string? quoteUrl,
        string? phone)
    {
        // Minimal escaper rather than WebUtility.HtmlEncode: that one turns every
        // non-ASCII character into a numeric entity, which would mangle õ/ä/ü/ų and
        // Cyrillic into unreadable soup in a UTF-8 email (and would let an accented
        // customer name slip past a plain "must not contain" check). Escaping
        // & < > " is sufficient for text nodes and double-quoted attributes, which
        // is all this template produces.
        static string E(string value) => value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
        static string Multiline(string value) => E(value).Replace("\n", "<br>");

        var urgentHtml = urgentLine is null
            ? ""
            : $"""
                     <table width="100%" cellpadding="0" cellspacing="0" style="background:#FFF8E1;border-left:4px solid #FF8F00;border-radius:4px;margin:0 0 20px;">
                       <tr><td style="padding:12px 16px;color:#E65100;font-size:15px;font-weight:700;">{E(urgentLine)}</td></tr>
                     </table>
               """;

        var factRows = string.Concat(facts.Select(f => $"""
                       <tr>
                         <td style="padding:6px 12px 6px 0;color:{MutedText};font-size:14px;vertical-align:top;white-space:nowrap;">{E(f.Label)}</td>
                         <td style="padding:6px 0;color:{BodyText};font-size:15px;vertical-align:top;">{Multiline(f.Value)}</td>
                       </tr>
               """));

        var ctaHtml = quoteUrl is null
            ? ""
            : $"""
                     <table cellpadding="0" cellspacing="0" style="margin:0 0 20px;">
                       <tr><td style="background:{Teal};border-radius:6px;">
                         <a href="{E(quoteUrl)}" style="display:inline-block;padding:14px 28px;color:#ffffff;font-size:16px;font-weight:600;text-decoration:none;">{E(t.OutreachQuoteCta)}</a>
                       </td></tr>
                     </table>
                     <p style="margin:0 0 20px;color:{MutedText};font-size:13px;word-break:break-all;">{E(quoteUrl)}</p>
               """;

        var phoneHtml = phone is null
            ? ""
            : $"""
                     <p style="margin:4px 0 0;color:{MutedText};font-size:14px;">{E(t.OutreachPhoneLabel)}: {E(phone)}</p>
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
                        <p style="margin:0 0 12px;color:{BodyText};font-size:16px;">{E(t.OutreachGreeting)}</p>
                        <p style="margin:0 0 20px;color:{BodyText};font-size:15px;line-height:1.6;">{E(t.OutreachIntro)}</p>
            {urgentHtml}
                        <table width="100%" cellpadding="0" cellspacing="0" style="margin:0 0 20px;">
            {factRows}
                        </table>
                        <p style="margin:0 0 20px;color:{BodyText};font-size:15px;line-height:1.6;">{E(t.OutreachAsk)}</p>
            {ctaHtml}
                        <p style="margin:0 0 24px;color:{BodyText};font-size:15px;line-height:1.6;">{E(t.OutreachReplyAlternative)}</p>
                        <p style="margin:0;color:{MutedText};font-size:14px;line-height:1.6;">{Multiline(t.OutreachSignature)}</p>
            {phoneHtml}
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
