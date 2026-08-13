using Ruumly.Backend.Models;

namespace Ruumly.Backend.Helpers;

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
/// </summary>
public static class CustomerRequestAckComposer
{
    public static (string Subject, string TextBody) Compose(
        DemandLead lead, string categoryLabel, string? contactUrl)
    {
        var t    = EmailTranslations.For(lead.Language);
        var name = string.IsNullOrWhiteSpace(lead.Name) ? null : lead.Name.Trim();

        var lines = new List<string>
        {
            name is null ? t.AckGreetingNoName : t.AckGreeting(name),
            "",
            t.AckReceived,
            "",
            // Read the request back to them. It is the cheapest possible proof
            // that a human-shaped thing arrived rather than a form post, and it
            // is where they will spot their own typo in a date or a city.
            t.AckSummaryHeading,
            $"- {t.AckLabelService}: {categoryLabel}",
            $"- {t.AckLabelCity}: {Describe(lead.City, lead.ToCity)}",
            $"- {t.AckLabelDate}: {lead.NeedDate?.ToString("dd.MM.yyyy") ?? t.AckDateAsap}",
        };

        if (!string.IsNullOrWhiteSpace(lead.Details))
            lines.Add($"- {t.AckLabelDetails}: {lead.Details.Trim()}");

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

        return (t.AckSubject, string.Join("\n", lines));
    }

    private static string Describe(string? from, string? to) =>
        string.IsNullOrWhiteSpace(to) ? from ?? "—" : $"{from} → {to}";
}
