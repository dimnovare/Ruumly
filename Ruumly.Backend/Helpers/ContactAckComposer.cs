namespace Ruumly.Backend.Helpers;

/// <summary>The contact-form receipt, in both formats an email client may render.</summary>
public sealed record ContactAckMessage(string Subject, string TextBody, string HtmlBody);

/// <summary>
/// The receipt someone gets the moment they send the public contact form.
///
/// Until 2026-08-16 the form produced exactly one email — to the team — and
/// nothing at all to the person who wrote it. That is the same gap
/// <see cref="CustomerRequestAckComposer"/> closed on the concierge intake three
/// days earlier, still open on the other public form, and it shows up in the
/// inbox as messages whose entire content is the word "test": a partner who had
/// just claimed their directory profile writing in to find out whether anything
/// is on the other end. Answering that question is the whole job of this mail.
///
/// Like the concierge receipt it promises NO response time. A deadline invented
/// by a queue and kept by a human is a deadline that gets broken; "a person
/// reads this and will answer you" is the honest version and is what the sender
/// actually wants to know.
///
/// It reads their own message back for the same reason the concierge ack reads
/// the request back: it is the cheapest possible proof that a human-shaped thing
/// arrived rather than a form post silently 200-ing.
/// </summary>
public static class ContactAckComposer
{
    // Same palette as the other composers, so every Ruumly email looks like it
    // came from one company.
    private const string Teal      = "#00897B";
    private const string BodyText  = "#455A64";
    private const string MutedText = "#78909C";
    private const string PageBg    = "#f5f5f5";

    /// <summary>
    /// How much of the sender's message is quoted back.
    ///
    /// The form accepts 5,000 characters, but this mail is delivered to an
    /// address NOBODY has verified — anyone can type a stranger's address into
    /// an anonymous form. Quoting the whole thing would make Ruumly's sender
    /// reputation a delivery service for 5 KB of attacker-chosen text. The cap
    /// keeps the proof-of-receipt (a sender recognises their own message from
    /// its opening) while bounding what the form can be made to carry.
    /// </summary>
    private const int MaxQuotedMessage = 600;

    public static ContactAckMessage Compose(
        string? language, string? name, string subject, string message)
    {
        var t         = EmailTranslations.For(language);
        var trimmed   = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var greeting  = trimmed is null ? t.AckGreetingNoName : t.AckGreeting(trimmed);

        var facts = new List<(string Label, string Value)>
        {
            (t.ContactAckLabelSubject, subject.Trim()),
            (t.ContactAckLabelMessage, Quote(message)),
        };

        return new(
            t.ContactAckSubject,
            BuildText(t, greeting, facts),
            BuildHtml(t, greeting, facts));
    }

    private static string Quote(string message)
    {
        var value = message.Trim();
        return value.Length > MaxQuotedMessage ? value[..MaxQuotedMessage] + "…" : value;
    }

    private static string BuildText(
        EmailTranslations.EmailStrings t,
        string greeting,
        IReadOnlyList<(string Label, string Value)> facts)
    {
        var lines = new List<string>
        {
            greeting, "", t.ContactAckReceived, "", t.ContactAckSummaryHeading,
        };
        foreach (var (label, value) in facts)
            lines.Add($"- {label}: {value}");

        lines.Add("");
        lines.Add(t.ContactAckReply);
        lines.Add("");
        lines.Add(t.AckSignature);

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Minimal inline-styled HTML: no external CSS, no images, no tracking
    /// pixels. Every interpolated value is escaped — the subject and the message
    /// are raw free text typed by an anonymous visitor.
    /// </summary>
    private static string BuildHtml(
        EmailTranslations.EmailStrings t,
        string greeting,
        IReadOnlyList<(string Label, string Value)> facts)
    {
        // Minimal escaper rather than WebUtility.HtmlEncode, which would turn
        // every õ/ä/ü/ų and every Cyrillic character into a numeric entity.
        // Escaping & < > " covers text nodes and double-quoted attributes, which
        // is all this template emits.
        static string E(string value) => value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
        static string Multiline(string value) => E(value).Replace("\n", "<br>");

        var factRows = string.Concat(facts.Select(f => $"""
                       <tr>
                         <td style="padding:6px 12px 6px 0;color:{MutedText};font-size:14px;vertical-align:top;white-space:nowrap;">{E(f.Label)}</td>
                         <td style="padding:6px 0;color:{BodyText};font-size:15px;vertical-align:top;">{Multiline(f.Value)}</td>
                       </tr>
               """));

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
                        <p style="margin:0 0 20px;color:{BodyText};font-size:15px;line-height:1.6;">{E(t.ContactAckReceived)}</p>
                        <p style="margin:0 0 8px;color:{MutedText};font-size:14px;font-weight:700;">{E(t.ContactAckSummaryHeading)}</p>
                        <table width="100%" cellpadding="0" cellspacing="0" style="margin:0 0 20px;">
            {factRows}
                        </table>
                        <p style="margin:0 0 20px;color:{BodyText};font-size:15px;line-height:1.6;">{E(t.ContactAckReply)}</p>
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
}
