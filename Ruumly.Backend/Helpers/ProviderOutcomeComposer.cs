using System.Globalization;
using Ruumly.Backend.Models;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// The three letters a provider who QUOTED is owed, and which the system used
/// to keep to itself.
///
/// WHY THIS EXISTS. A provider receives a cold request, does unpaid pricing work
/// on it, sends a number — and then hears nothing, ever. The only party told
/// what happened was the ops inbox (<c>OffersController.ChooseOption</c>). That
/// is the fastest way there is to lose a second quote from a business that gave
/// you a first one, and it was the largest remaining hole in the concierge loop
/// after the decline path shipped.
///
/// THREE MOMENTS:
/// <list type="bullet">
/// <item><b>Sent</b> — their price went to the customer alongside the others;
/// nothing is required of them, and the wait now has a known shape.</item>
/// <item><b>Won</b> — the customer picked them.</item>
/// <item><b>Lost</b> — the customer picked somebody else.</item>
/// </list>
///
/// WHAT THESE LETTERS DELIBERATELY DO NOT CARRY:
/// <list type="bullet">
/// <item><b>The winning price, in the losing letter.</b> That number is the
/// winner's confidential commercial data. Handing it to the businesses they
/// just beat teaches the whole panel to either lowball or stop quoting
/// honestly — which costs Ruumly the one asset the concierge model runs on.
/// Founder-confirmed 2026-08-20: losers are told somebody else was chosen, not
/// who and not for how much.</item>
/// <item><b>The customer's identity.</b> Same rule the outreach letter follows:
/// the admin brokers the introduction, and a provider learns who the customer
/// is only once a booking is confirmed.</item>
/// <item><b>Any competitor's name.</b> Not in any of the three.</item>
/// </list>
///
/// Language comes from <see cref="ProviderOutreachComposer.LanguageFor"/>
/// (i.e. <c>Supplier.Country</c>), never from the customer's
/// <c>Offer.Language</c> — a Latvian mover reads Latvian even when the customer
/// wrote in Russian. Estonian is FORMAL (<c>teie</c>) like every other
/// provider-facing letter in this file's neighbourhood.
/// </summary>
public static class ProviderOutcomeComposer
{
    /// <summary>Which of the three letters to compose.</summary>
    public enum Outcome
    {
        /// <summary>Their quote has just been forwarded to the customer.</summary>
        OfferSent,
        /// <summary>The customer chose this provider.</summary>
        Won,
        /// <summary>The customer chose a different provider.</summary>
        Lost,
    }

    /// <summary>Same operator identity line the outreach letter prints.</summary>
    private const string OperatorLegalLine =
        "Diip Solutions OÜ · reg 17527757 · Uus-Sadama tn 15-2, 10120 Tallinn";

    /// <summary>A composed letter: what to put in the two fields that matter.</summary>
    public readonly record struct Letter(string Subject, string TextBody);

    /// <summary>
    /// Compose one provider letter.
    /// </summary>
    /// <param name="outcome">Which of the three moments this is.</param>
    /// <param name="supplier">Recipient — decides the language.</param>
    /// <param name="lead">The request, for the city and the reference handle.</param>
    /// <param name="quotedAmount">
    /// This provider's OWN price, echoed back so the letter is unambiguously
    /// about the job they priced (two live Tallinn→Tartu moves otherwise produce
    /// identical letters). Null when the option carries no amount — the line is
    /// then simply omitted rather than printed empty.
    /// </param>
    /// <param name="quotedUnit">
    /// The unit as stored on the option. NOT assumed to be the provider's own
    /// words: <c>QuoteController</c> normalizes a submitted unit into the
    /// CUSTOMER's language before storing it on the option (the provider's
    /// literal string stays on <c>ProviderOutreach.QuotedUnit</c>). Left
    /// unhandled, a Latvian mover's "vienreizējs" would come back to them as the
    /// Estonian "korra" simply because the customer wrote in Estonian — so this
    /// is re-rendered into the RECIPIENT's language below.
    /// </param>
    public static Letter Compose(
        Outcome outcome,
        Supplier supplier,
        DemandLead lead,
        decimal? quotedAmount = null,
        string? quotedUnit = null)
    {
        var language = ProviderOutreachComposer.LanguageFor(supplier);
        var t = EmailTranslations.For(language);
        var city = string.IsNullOrWhiteSpace(lead.City) ? "" : lead.City.Trim();
        var reference = ProviderOutreachComposer.Reference(lead.Id);

        var (subjectTpl, body) = outcome switch
        {
            Outcome.OfferSent => (
                t.ProviderOfferSentSubjectTpl,
                new[] { t.ProviderOfferSentIntro, t.ProviderOfferSentWaiting, t.ProviderOfferSentNoAction }),
            Outcome.Won => (
                t.ProviderWonSubjectTpl,
                new[] { t.ProviderWonIntro, t.ProviderWonNext }),
            Outcome.Lost => (
                t.ProviderLostSubjectTpl,
                new[] { t.ProviderLostIntro, t.ProviderLostThanks, t.ProviderLostNext }),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };

        // Reference in the subject for the same reason every other provider mail
        // carries one: Reply-To is a single shared inbox, and two live requests
        // in one city produce otherwise identical subjects.
        //
        // A blank City is possible (the intake requires one, but an admin edit or
        // an older row need not have it), and a raw Replace would then produce a
        // subject that opens with a bare ": " — the exact broken-merge-field look
        // the greeting rule elsewhere exists to avoid. Fall back to the
        // destination, then to dropping the prefix altogether.
        var subjectPlace = city.Length > 0
            ? city
            : (lead.ToCity ?? "").Trim();
        var subjectBody = subjectPlace.Length > 0
            ? subjectTpl.Replace("{city}", subjectPlace)
            : StripCityPrefix(subjectTpl);
        var subject = $"{subjectBody} [{reference}]";

        var lines = new List<string>
        {
            GreetingFor(t, supplier),
            "",
        };
        foreach (var paragraph in body)
        {
            lines.Add(paragraph);
            lines.Add("");
        }

        // Their own number, so the letter names the job they priced — with the
        // unit rendered in THEIR language, not the customer's (see quotedUnit).
        var priceLine = FormatPrice(
            quotedAmount,
            PriceUnitNormalizer.ToCustomerLanguage(quotedUnit, language) ?? quotedUnit);
        if (priceLine is not null)
        {
            lines.Add($"{t.ProviderOutcomeQuoteLabel}: {priceLine}");
            lines.Add("");
        }

        lines.Add($"{t.OutreachLabelLocation}: {RouteFor(lead)}");
        lines.Add("");
        lines.Add(t.OutreachSignature);
        lines.Add("");
        lines.Add(OperatorLegalLine);

        return new Letter(subject, string.Join("\n", lines));
    }

    /// <summary>
    /// Named greeting where the name is usable, plain greeting otherwise —
    /// mirrors ProviderOutreachComposer's rule so the two letters read as coming
    /// from the same sender.
    /// </summary>
    private static string GreetingFor(EmailTranslations.EmailStrings t, Supplier supplier)
    {
        var name = supplier.Name?.Trim();
        return !string.IsNullOrWhiteSpace(name) && name.Length <= 60
            ? t.OutreachGreetingTpl.Replace("{company}", name)
            : t.OutreachGreeting;
    }

    /// <summary>
    /// "150 € / vienreizējs". Invariant number formatting with the unit printed
    /// exactly as the provider submitted it — this is their own string coming
    /// back to them, so re-interpreting it would be the one way to make it wrong.
    /// </summary>
    private static string? FormatPrice(decimal? amount, string? unit)
    {
        if (amount is not { } value) return null;
        var money = value.ToString("0.##", CultureInfo.InvariantCulture) + " €";
        var trimmedUnit = unit?.Trim();
        return string.IsNullOrWhiteSpace(trimmedUnit) ? money : $"{money} / {trimmedUnit.TrimStart('/', ' ')}";
    }

    /// <summary>
    /// Drop a leading "{city}: " (or "{city} - ") from a subject template when
    /// there is no place to put in it, so the line still reads as a sentence.
    /// </summary>
    private static string StripCityPrefix(string template)
    {
        var marker = template.IndexOf("{city}", StringComparison.Ordinal);
        if (marker < 0) return template;
        var rest = template[(marker + "{city}".Length)..];
        return rest.TrimStart(':', '-', '—', ' ') is { Length: > 0 } trimmed
            ? char.ToUpperInvariant(trimmed[0]) + trimmed[1..]
            : template;
    }

    /// <summary>City, or "City → City" for a move — no street address ever.</summary>
    private static string RouteFor(DemandLead lead) =>
        string.IsNullOrWhiteSpace(lead.ToCity)
            ? (lead.City ?? "").Trim()
            : $"{(lead.City ?? "").Trim()} → {lead.ToCity.Trim()}";
}
