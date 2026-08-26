using System.Text;
using System.Text.RegularExpressions;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// Renders an operator's lead message into the house email layout.
///
/// The message an operator types stays PLAIN TEXT all the way through the API:
/// POST /admin/leads/{id}/messages still rejects '&lt;' and '&gt;' outright, so
/// nobody — not a compromised admin session, not a pasted signature — can put
/// markup on the wire. The HTML is derived here, from text, and every character
/// of it is escaped on the way in. That keeps the injection surface exactly where
/// it was while letting a customer receive something better than a wall of
/// monospace.
///
/// The structure it understands is the structure people already type:
///   • blank line          — paragraph break
///   • a line in ALL CAPS  — section heading
///   • a line starting     — bullet
///     "• " or "- "
/// and inline it turns phone numbers, addresses and URLs into tel:, mailto: and
/// https: links. The phone links are the point on mobile: a hand-off letter full
/// of numbers a customer has to copy out by hand is a hand-off she will not make.
///
/// Text that uses none of this still renders correctly — as paragraphs — so no
/// existing message needs rewriting and no operator has to learn a syntax.
/// </summary>
public static class LeadMessageComposer
{
    /// <summary>
    /// One pass over the raw text, so a link is never scanned for links again —
    /// running three regexes in sequence over a growing string would happily
    /// match an address inside an href it had just written.
    /// </summary>
    private static readonly Regex Inline = new(
        @"(?<url>https?://[^\s<>""]+)" +
        @"|(?<mail>[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,})" +
        @"|(?<tel>\+\d[\d ]{5,15}\d)",
        RegexOptions.Compiled);

    public static string BuildHtml(string text)
    {
        var body = new StringBuilder();
        // \r\n and \n both; an operator's paste can carry either.
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var bullets = new List<string>();
        void FlushBullets()
        {
            if (bullets.Count == 0) return;
            body.Append($"""<table width="100%" cellpadding="0" cellspacing="0" style="margin:0 0 18px;">""");
            foreach (var item in bullets)
            {
                body.Append($"""
                    <tr>
                      <td width="14" style="padding:3px 0 3px 0;color:{EmailBrand.Teal};font-size:15px;vertical-align:top;">&bull;</td>
                      <td style="padding:3px 0;color:{EmailBrand.BodyText};font-size:15px;line-height:1.6;">{item}</td>
                    </tr>
                    """);
            }
            body.Append("</table>");
            bullets.Clear();
        }

        var paragraph = new List<string>();
        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            body.Append($"""<p style="margin:0 0 16px;color:{EmailBrand.BodyText};font-size:15px;line-height:1.6;">{string.Join("<br>", paragraph)}</p>""");
            paragraph.Clear();
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                FlushBullets();
                continue;
            }

            if (trimmed.StartsWith("• ", StringComparison.Ordinal)
             || trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                FlushParagraph();
                bullets.Add(Linkify(trimmed[2..].Trim()));
                continue;
            }

            if (IsHeading(trimmed))
            {
                FlushParagraph();
                FlushBullets();
                body.Append($"""<p style="margin:0 0 8px;color:{EmailBrand.Teal};font-size:13px;font-weight:700;letter-spacing:0.6px;">{Linkify(trimmed)}</p>""");
                continue;
            }

            FlushBullets();
            paragraph.Add(Linkify(trimmed));
        }

        FlushParagraph();
        FlushBullets();
        return EmailBrand.Page(body.ToString());
    }

    /// <summary>
    /// A line the operator shouted is a section heading. Compared against its own
    /// uppercase rather than tested per character so that Estonian Õ/Ä/Ü and
    /// Cyrillic behave like any other letter, and required to contain a letter so
    /// that "+372 5214653" or "2026" is never mistaken for one.
    /// </summary>
    private static bool IsHeading(string line) =>
        line.Length <= 70
        && line.Any(char.IsLetter)
        && line == line.ToUpperInvariant();

    private static string Linkify(string value)
    {
        var html = new StringBuilder();
        var at = 0;

        foreach (Match m in Inline.Matches(value))
        {
            html.Append(EmailBrand.E(value[at..m.Index]));
            var shown = EmailBrand.E(m.Value);

            if (m.Groups["url"].Success)
            {
                html.Append($"""<a href="{shown}" style="color:{EmailBrand.Teal};">{shown}</a>""");
            }
            else if (m.Groups["mail"].Success)
            {
                html.Append($"""<a href="mailto:{shown}" style="color:{EmailBrand.Teal};">{shown}</a>""");
            }
            else
            {
                // tel: wants no spaces; the reader still sees them.
                var dialable = EmailBrand.E(m.Value.Replace(" ", ""));
                html.Append($"""<a href="tel:{dialable}" style="color:{EmailBrand.Teal};text-decoration:none;font-weight:700;">{shown}</a>""");
            }

            at = m.Index + m.Length;
        }

        html.Append(EmailBrand.E(value[at..]));
        return html.ToString();
    }
}
