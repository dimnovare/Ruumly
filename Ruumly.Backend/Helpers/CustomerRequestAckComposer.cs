using Ruumly.Backend.Models;

namespace Ruumly.Backend.Helpers;

/// <summary>The customer's receipt, in both formats an email client may render.</summary>
public sealed record CustomerAckMessage(string Subject, string TextBody, string HtmlBody);

/// <summary>
/// The receipt a customer gets the moment they submit a request.
///
/// Until 2026-08-13 they got nothing at all. The only mail leaving the intake
/// went to the ops inbox, and the first thing a customer ever heard from Ruumly
/// was the offer — days later, from an address they had never corresponded
/// with, straight into a spam filter — while the success screen had promised
/// "2-3 offers, usually within 24 hours".
///
/// What that cost was not politeness. It was:
/// <list type="bullet">
/// <item>no proof the request arrived, on a form with no account behind it;</item>
/// <item>no thread to reply to when the date moves or a detail changes — the
/// one channel back from the customer was an address we never exercised;</item>
/// <item>and no way for them to tell a silent success from a silent failure,
/// which mattered enormously while a city-matching bug was quietly emailing
/// nobody.</item>
/// </list>
///
/// It deliberately does NOT repeat the 24-hour promise. Some requests reach no
/// provider automatically — a multi-service ask is routed by hand, and a city we
/// cannot match still needs a human. Restating a deadline in the one message
/// that proves we received the request would turn an honest wait into a broken
/// promise. It says what happens next and invites a reply; that is all it can
/// keep.
///
/// Since 2026-08-14 it is sent as HTML with a plain-text fallback. It was text
/// only, while the COLD email we send to strangers was branded HTML — so the
/// customer's sole proof of receipt looked less legitimate than unsolicited
/// mail, which is exactly backwards for the message whose entire job is to be
/// believed.
/// </summary>
public static class CustomerRequestAckComposer
{
    // Same palette as ProviderOutreachComposer and the password-reset mail, so
    // every Ruumly email looks like it came from one company.
    private const string Teal      = "#00897B";
    private const string BodyText  = "#455A64";
    private const string MutedText = "#78909C";
    private const string PageBg    = "#f5f5f5";

    public static CustomerAckMessage Compose(
        DemandLead lead, string categoryLabel, string? contactUrl)
    {
        var t    = EmailTranslations.For(lead.Language);
        var name = string.IsNullOrWhiteSpace(lead.Name) ? null : lead.Name.Trim();

        var greeting = name is null ? t.AckGreetingNoName : t.AckGreeting(name);

        // Read the request back to them. It is the cheapest possible proof that
        // a human-shaped thing arrived rather than a form post, and it is where
        // they will spot their own typo in a date or a city.
        var facts = new List<(string Label, string Value)>
        {
            (t.AckLabelService, categoryLabel),
            (t.AckLabelCity,    Describe(lead.City, lead.ToCity)),
            (t.AckLabelDate,    lead.NeedDate?.ToString("dd.MM.yyyy") ?? t.AckDateAsap),
        };
        if (!string.IsNullOrWhiteSpace(lead.Details))
            facts.Add((t.AckLabelDetails, lead.Details.Trim()));

        return new(
            t.AckSubject,
            BuildText(t, greeting, facts, contactUrl),
            BuildHtml(t, greeting, facts, contactUrl));
    }

    private static string BuildText(
        EmailTranslations.EmailStrings t,
        string greeting,
        IReadOnlyList<(string Label, string Value)> facts,
        string? contactUrl)
    {
        var lines = new List<string> { greeting, "", t.AckReceived, "", t.AckSummaryHeading };
        foreach (var (label, value) in facts)
            lines.Add($"- {label}: {value}");

        lines.Add("");
        lines.Add(t.AckWhatNext);
        lines.Add("");
        lines.Add(t.AckReply);

        if (!string.IsNullOrWhiteSpace(contactUrl))
        {
            lines.Add("");
            lines.Add(t.AckContact(contactUrl));
        }

        lines.Add("");
        lines.Add(t.AckSignature);

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Minimal inline-styled HTML: no external CSS, no images, no tracking
    /// pixels — a branded header, the request read back as a table, and the
    /// signature. Every interpolated value is escaped; the details field is raw
    /// customer free text and the name is whatever they typed.
    /// </summary>
    private static string BuildHtml(
        EmailTranslations.EmailStrings t,
        string greeting,
        IReadOnlyList<(string Label, string Value)> facts,
        string? contactUrl)
    {
        // Minimal escaper rather than WebUtility.HtmlEncode: that one turns
        // every non-ASCII character into a numeric entity, which would mangle
        // õ/ä/ü/ų and Cyrillic into unreadable soup in a UTF-8 email — and this
        // message is read by Estonian, Russian, Latvian and Lithuanian
        // customers looking at their OWN name. Escaping & < > " is sufficient
        // for text nodes and double-quoted attributes, which is all this
        // template produces.
        static string E(string value) => value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
        static string Multiline(string value) => E(value).Replace("\n", "<br>");

        static string Linkified(string sentence, string url)
        {
            var at = sentence.IndexOf(url, StringComparison.Ordinal);
            if (at < 0) return E(sentence);
            return E(sentence[..at])
                 + $"""<a href="{E(url)}" style="color:{Teal};">{E(url)}</a>"""
                 + E(sentence[(at + url.Length)..]);
        }

        var factRows = string.Concat(facts.Select(f => $"""
                       <tr>
                         <td style="padding:6px 12px 6px 0;color:{MutedText};font-size:14px;vertical-align:top;white-space:nowrap;">{E(f.Label)}</td>
                         <td style="padding:6px 0;color:{BodyText};font-size:15px;vertical-align:top;">{Multiline(f.Value)}</td>
                       </tr>
               """));

        var contactHtml = string.IsNullOrWhiteSpace(contactUrl)
            ? ""
            : $"""
                        <p style="margin:0 0 24px;color:{BodyText};font-size:15px;line-height:1.6;">{Linkified(t.AckContact(contactUrl), contactUrl)}</p>
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
                        <p style="margin:0 0 12px;color:{BodyText};font-size:16px;">{E(greeting)}</p>
                        <p style="margin:0 0 20px;color:{BodyText};font-size:15px;line-height:1.6;">{E(t.AckReceived)}</p>
                        <p style="margin:0 0 8px;color:{MutedText};font-size:14px;font-weight:700;">{E(t.AckSummaryHeading)}</p>
                        <table width="100%" cellpadding="0" cellspacing="0" style="margin:0 0 20px;">
            {factRows}
                        </table>
                        <p style="margin:0 0 20px;color:{BodyText};font-size:15px;line-height:1.6;">{E(t.AckWhatNext)}</p>
                        <p style="margin:0 0 20px;color:{BodyText};font-size:15px;line-height:1.6;">{E(t.AckReply)}</p>
            {contactHtml}
                        <p style="margin:0;color:{MutedText};font-size:14px;line-height:1.6;">{Multiline(t.AckSignature)}</p>
                      </td>
                    </tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }

    private static string Describe(string? from, string? to) =>
        string.IsNullOrWhiteSpace(to) ? from ?? "—" : $"{from} → {to}";
}
