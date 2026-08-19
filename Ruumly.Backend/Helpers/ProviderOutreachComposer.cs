using Ruumly.Backend.Constants;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Helpers;

public static class ProviderOutreachComposer
{
    /// <summary>
    /// A need date at most this many days away is flagged as urgent — in the
    /// subject line and at the top of the body. Next-day demand is the dominant
    /// pattern in the concierge funnel, and a provider who only skims the
    /// subject must still see it.
    ///
    /// The window is CLOSED AT BOTH ENDS (see <c>isUrgent</c> below). It used to
    /// be open at the past end — "anything up to today+3" — which is not the
    /// same rule at all: outreach is re-composed every time an admin fans a lead
    /// out to another provider, days or weeks after it arrived, so a lead whose
    /// date had already gone by went out stamped
    /// "URGENT: the customer needs this by {a date in the past}". That is the
    /// one sentence in the whole email a recipient can check against their own
    /// calendar, and getting it wrong proves the sender is a machine that does
    /// not read its own mail. The intake rejects past dates on the way in
    /// (SupportController), so this end of the window only ever fires on the
    /// passage of time — which is exactly why it has to be enforced here too.
    /// </summary>
    public const int UrgentWithinDays = 3;

    /// <summary>
    /// Who is actually writing, printed once in the footer of every provider
    /// email.
    ///
    /// A cold recipient's first question about an unsolicited business email is
    /// whether there is a company behind it. "Ruumly" is a brand they have never
    /// heard of; a registry code is something an Estonian business owner can
    /// paste into the äriregister in five seconds and settle the question
    /// themselves. Cheapest legitimacy signal available, and the only one in
    /// this email that does not depend on believing us.
    ///
    /// Language-neutral on purpose — a legal name, a registry code and a street
    /// address are the same string in all five languages, and translating them
    /// would be five chances to get a legal identity wrong.
    ///
    /// PROVENANCE: founder-confirmed 2026-08-14. The VAT number, IBAN and phone
    /// once associated with the old operating company are NOT verified against
    /// this entity and must not be added here without re-confirmation.
    /// </summary>
    private const string OperatorLegalLine =
        "Diip Solutions OÜ · reg 17527757 · Uus-Sadama tn 15-2, 10120 Tallinn";

    /// <summary>
    /// Longest provider name we will paste into a greeting. Directory rows are
    /// imported from public sources and a few carry a whole legal description
    /// rather than a trading name; a 200-character "Tere, ...!" reads as a
    /// broken merge field, which is the exact impression the greeting exists to
    /// avoid. Over the limit we fall back to the plain greeting.
    /// </summary>
    private const int MaxGreetingNameLength = 60;

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
    ///
    /// THE RECIPIENT IS COLD. Almost every address this reaches belongs to a
    /// business that has no relationship with Ruumly, did not ask to be in the
    /// directory, and is being asked to do unpaid pricing work for a stranger.
    /// So the letter has to answer, before the ask: who is writing, why this
    /// company, when the request came in, what answering costs, what happens to
    /// the price, and how to make the mail stop. Every one of those is a fact we
    /// already hold — none of it is a promise about volume, response times or
    /// other providers, and there is nothing here we could not show a recipient
    /// the evidence for.
    ///
    /// Since 2026-08 no support phone is printed: a provider with a question
    /// replies to the mail or uses the contact page (<see cref="FrontendUrl.Contact"/>).
    /// </summary>
    public static ProviderOutreachMessage Compose(
        DemandLead lead,
        Supplier supplier,
        string? appUrl = null,
        string? quoteToken = null)
        // The supplier's own name travels into the greeting. It used to be read
        // for its country and then thrown away, so eighteen Viljandi storage
        // providers received eighteen letters all opening "Tere!" about
        // near-identical requests — addressed to nobody, from a name they did
        // not know. We are holding the company name the whole time; not using it
        // is the difference between a letter and a mailshot.
        => ComposeInLanguage(LanguageFor(supplier), lead, appUrl, quoteToken, supplier.Name);

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
        string? providerName = null)
    {
        var t = EmailTranslations.For(language);
        var route = string.IsNullOrWhiteSpace(lead.ToCity)
            ? lead.City
            : $"{lead.City} → {lead.ToCity}";
        var category = LeadServiceLabel.For(t, lead);

        // "—" tells a provider nothing and reads as a broken template. An absent
        // date/details becomes a real instruction instead ("as soon as possible —
        // we'll confirm it" / "not specified — we'll check with the customer").
        var date = lead.NeedDate?.ToString("yyyy-MM-dd");
        // Three distinct states, not two. A named day; an explicit "any day suits
        // me", which a provider can quote against; and silence, which they cannot.
        // The middle one used to be reported as the third, telling a provider the
        // customer had not answered a question they had in fact answered.
        var dateText = date
            ?? (ServiceCategories.HasFlexibleDate(lead.Query)
                ? t.OutreachDateFlexible
                : t.OutreachDateAsap);
        var details  = string.IsNullOrWhiteSpace(lead.Details)
            ? t.OutreachDetailsMissing
            : lead.Details.Trim();

        var today      = DateTime.UtcNow.Date;
        var isUrgent   = lead.NeedDate is { } needDate
                      && needDate.Date >= today
                      && needDate.Date <= today.AddDays(UrgentWithinDays);
        var urgentLine = isUrgent ? t.OutreachUrgent(date!) : null;

        // Who we are writing to. Trimmed and length-capped, then either a named
        // greeting or the plain one — never "Tere, !" and never a greeting
        // carrying half a legal description.
        var company  = providerName?.Trim();
        var greeting = company is { Length: > 0 } && company.Length <= MaxGreetingNameLength
            ? t.OutreachGreetingTo(company)
            : t.OutreachGreeting;

        // When the request actually arrived, from the lead's own row.
        //
        // This replaces an assertion with a fact. The intro used to say "this is
        // a real customer request", which is the one claim a lead-generation bot
        // would also make and which the recipient has no way to check. A date is
        // different in kind: it is specific, it is falsifiable against the rest
        // of the mail, and — because outreach is re-composed on every fan-out —
        // it is also the line that stops a three-week-old request from arriving
        // dressed as today's news.
        var submitted = lead.CreatedAt.ToString("yyyy-MM-dd");

        // ── Subject ───────────────────────────────────────────────────────────
        //
        // PLACE FIRST, then service, then the word for what this is. A phone lock
        // screen shows roughly thirty-five characters, and the template used to
        // spend the first nine of them on "Ruumly — " before naming the request
        // type and only then, last, the city — so a Tallinn → Tartu move arrived
        // as "Ruumly — kliendipäring: Kolimine,…" and the recipient could not see
        // either end of the route without opening it. The two facts that decide
        // whether a provider opens the mail at all are "is this my area" and "is
        // this what I do"; those now come first and both survive the truncation.
        //
        // Dropping the brand costs nothing: it is the From display name
        // (Email:FromName), the HTML header, the greeting and the signature
        // already. It was the one word in the subject that told the recipient
        // nothing about the request.
        //
        // The lead reference is what makes a provider's REPLY usable. Reply-To is
        // one shared ops inbox and the rest of the subject is only place +
        // service, so two live Tallinn → Tartu moving requests produce
        // byte-identical subjects. Replying is a primary action this email asks
        // for, and an answer used to arrive with nothing on it that said which
        // customer it was about — ops had to guess, or ask, on the one
        // interaction where speed is the entire product.
        //
        // Deliberately the smallest thing that works: eight hex characters of the
        // lead's own id, appended AFTER the localized subject (so no translation
        // carries a placeholder), language-neutral, and preserved by every mail
        // client's "Re:" prefixing. It is a lookup key, not a credential — it
        // grants nothing on its own, unlike the per-recipient quote token.
        var subject = $"{t.OutreachSubject(category, route)} [{Reference(lead.Id)}]";
        if (isUrgent) subject = $"{t.OutreachUrgentBadge}: {subject}";

        var quoteUrl = string.IsNullOrWhiteSpace(quoteToken)
            ? null
            : FrontendUrl.Localized(appUrl, language, $"quote/{quoteToken}");
        var questions = t.OutreachQuestions(FrontendUrl.Contact(appUrl, language));

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

        // The intake's scoping answers — home size, floor and lift, how long,
        // what is being hauled — as LOCALIZED fact lines.
        //
        // THIS IS THE POINT OF STORING THEM AS POSITIONS. The funnel used to
        // render each chip to a sentence in the CUSTOMER's language and glue it
        // into Details, which is printed verbatim below: a Russian-speaking
        // customer's answers arrived, in Russian, in an Estonian mover's inbox.
        // Resolving the wording here means the same lead reads Estonian to an
        // Estonian mover and Latvian to a Latvian one.
        //
        // Above Details on purpose: these are the facts a price is built from,
        // and Details is the customer's own free text, which closes the block.
        //
        // A question with no copy in this language is SKIPPED, never printed as
        // an id or a bare number. LeadScope already drops ids this build does
        // not know; this covers the narrower case of a known question whose
        // translation has not landed yet, and it must fail the same way —
        // quietly, one line short, with the request itself intact.
        //
        // ONE LINE PER QUESTION, even when the customer ticked several boxes.
        // Two questions are tick-all-that-apply — "a piano AND an aquarium" is a
        // sentence a mover needs to be able to read, and it was previously
        // flattened to "several of these", which is a guess dressed as an
        // answer. A second row under the same heading would read as the template
        // losing track of itself, so the chips are joined in the recipient's own
        // language instead (ScopeJoin).
        foreach (var answer in LeadScope.Answers(lead.ScopeJson))
        {
            if (t.ScopeLabel(answer.QuestionId) is not { } scopeLabel) continue;

            // An option with no wording drops out of the list rather than
            // taking the line with it: three ticked boxes and one retired chip
            // must still tell the provider about the other two.
            var worded = answer.Options
                .Select(option => t.ScopeOption(answer.QuestionId, option))
                .OfType<string>()
                .ToList();
            if (worded.Count == 0) continue;

            facts.Add((scopeLabel, t.ScopeJoin(worded)));
        }

        facts.Add((t.OutreachLabelDetails,  details));

        // Photos are NOT attached — they are pictures of somebody's home, and
        // they live behind the per-recipient quote token. Saying they exist is
        // what turns the CTA from a form into the thing the provider needs:
        // Adduco refused to quote a real move until it had them.
        var photoCount = LeadPhotos.Count(lead.PhotoKeysJson);
        if (photoCount > 0)
            facts.Add((t.OutreachLabelPhotos, t.OutreachPhotos(photoCount)));

        var copy = new OutreachCopy(
            greeting,
            t.OutreachProvenance(submitted),
            urgentLine,
            quoteUrl,
            questions,
            FrontendUrl.Contact(appUrl, language));

        return new(
            language,
            subject,
            BuildText(t, facts, copy),
            BuildHtml(t, facts, copy));
    }

    /// <summary>
    /// The short, human-quotable handle for a lead that goes in the subject line.
    /// Uppercase hex so it survives being read down a phone and typed into the
    /// admin search box.
    /// </summary>
    public static string Reference(Guid leadId) =>
        leadId.ToString("N")[..8].ToUpperInvariant();

    /// <summary>
    /// The parts of the letter that had to be resolved against this particular
    /// lead and recipient, as opposed to read straight off the translation
    /// table.
    ///
    /// A record rather than six more positional parameters on each builder: the
    /// text and HTML bodies must offer the SAME blocks in the SAME order — a
    /// provider reads whichever their client renders, and a paragraph present in
    /// one and missing from the other is a difference nobody would ever see in
    /// review. One shared shape makes them drift together or not at all.
    /// </summary>
    private sealed record OutreachCopy(
        string Greeting,
        string Provenance,
        string? UrgentLine,
        string? QuoteUrl,
        string Questions,
        string ContactUrl);

    /// <summary>
    /// The three answers this email accepts, in descending order of what they
    /// are worth to us, rendered after the fact table.
    ///
    /// WHY ALL THREE. Until now the letter asked for a price and offered no
    /// other way to respond, so a provider who could take the job but could not
    /// price it from what we sent — and a provider for whom the job was simply
    /// wrong — both had exactly one available action: nothing. Eighteen Viljandi
    /// contacts produced eighteen of them. Silence is also the one answer that
    /// teaches us nothing: a decline retires a candidate, a question tells ops
    /// what the intake failed to capture, and neither was reachable from here.
    ///
    /// Nothing here is new functionality. The "what is missing" path is the
    /// need-info action already on the quote page
    /// (POST /api/quote/{token}/need-info, shipped 2026-08-18 after Adduco
    /// answered a live Haapsalu move with exactly that); the email simply never
    /// mentioned that it existed. The decline sentence is the founder's own
    /// wording from the introduction campaign, reused verbatim.
    /// </summary>
    private static IEnumerable<string> Answers(EmailTranslations.EmailStrings t, string? quoteUrl)
    {
        // Only when there is a page to say it on: with no token minted there is
        // no quote page, and pointing at "the same page" would be pointing at
        // nothing.
        if (quoteUrl is not null) yield return t.OutreachCannotPrice;
        yield return t.OutreachReplyAlternative;
        yield return t.IntroIfNotSuitable;
    }

    private static string BuildText(
        EmailTranslations.EmailStrings t,
        IReadOnlyList<(string Label, string Value)> facts,
        OutreachCopy copy)
    {
        var lines = new List<string>
        {
            copy.Greeting,
            "",
            t.OutreachIntro,
            "",
            copy.Provenance,
            "",
        };

        if (copy.UrgentLine is not null)
        {
            lines.Add($"** {copy.UrgentLine} **");
            lines.Add("");
        }

        foreach (var (label, value) in facts)
            lines.Add($"{label}: {value}");

        lines.Add("");
        lines.Add(t.OutreachAsk);

        if (copy.QuoteUrl is not null)
        {
            lines.Add("");
            lines.Add($"{t.OutreachQuoteCta} → {copy.QuoteUrl}");
        }

        foreach (var answer in Answers(t, copy.QuoteUrl))
        {
            lines.Add("");
            lines.Add(answer);
        }

        lines.Add("");
        lines.Add(copy.Questions);
        lines.Add("");
        lines.Add(t.IntroOptOut(SupplierIntroComposer.OptOutKeyword));
        lines.Add("");
        lines.Add(t.OutreachSignature);
        lines.Add("");
        lines.Add(OperatorLegalLine);

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Minimal inline-styled HTML: no external CSS, no images, no tracking
    /// pixels — a branded header, who we are and why we are writing, the request
    /// facts, one CTA plus the two lighter answers, and a footer carrying the
    /// opt-out and the operating company. Every interpolated value is
    /// HTML-encoded: the details field is raw customer free text and the
    /// greeting carries an imported company name.
    ///
    /// The no-images rule is why the opt-out and the legal line are plain text
    /// in a bordered block rather than a footer graphic: an email that renders
    /// to an empty box when a client blocks remote content has no legitimacy
    /// signal at all.
    /// </summary>
    private static string BuildHtml(
        EmailTranslations.EmailStrings t,
        IReadOnlyList<(string Label, string Value)> facts,
        OutreachCopy copy)
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

        // The contact page is now the only channel besides replying, so its URL
        // has to be clickable: most clients do NOT auto-link a bare URL sitting
        // in an HTML body. Split the rendered sentence on the URL that was just
        // interpolated into it and wrap that one segment in an anchor.
        static string Linkified(string sentence, string url)
        {
            var at = sentence.IndexOf(url, StringComparison.Ordinal);
            if (at < 0) return E(sentence);
            return E(sentence[..at])
                 + $"""<a href="{E(url)}" style="color:{Teal};">{E(url)}</a>"""
                 + E(sentence[(at + url.Length)..]);
        }

        var urgentHtml = copy.UrgentLine is null
            ? ""
            : $"""
                     <table width="100%" cellpadding="0" cellspacing="0" style="background:#FFF8E1;border-left:4px solid #FF8F00;border-radius:4px;margin:0 0 20px;">
                       <tr><td style="padding:12px 16px;color:#E65100;font-size:15px;font-weight:700;">{E(copy.UrgentLine)}</td></tr>
                     </table>
               """;

        var factRows = string.Concat(facts.Select(f => $"""
                       <tr>
                         <td style="padding:6px 12px 6px 0;color:{MutedText};font-size:14px;vertical-align:top;white-space:nowrap;">{E(f.Label)}</td>
                         <td style="padding:6px 0;color:{BodyText};font-size:15px;vertical-align:top;">{Multiline(f.Value)}</td>
                       </tr>
               """));

        var ctaHtml = copy.QuoteUrl is null
            ? ""
            : $"""
                     <table cellpadding="0" cellspacing="0" style="margin:0 0 20px;">
                       <tr><td style="background:{Teal};border-radius:6px;">
                         <a href="{E(copy.QuoteUrl)}" style="display:inline-block;padding:14px 28px;color:#ffffff;font-size:16px;font-weight:600;text-decoration:none;">{E(t.OutreachQuoteCta)}</a>
                       </td></tr>
                     </table>
                     <p style="margin:0 0 20px;color:{MutedText};font-size:13px;word-break:break-all;">{E(copy.QuoteUrl)}</p>
               """;

        // The signature now carries the public site address, and it has to be
        // clickable for exactly the reason the contact link does — most clients
        // will not auto-link a bare URL in an HTML body, and a "check us for
        // yourself" signal nobody can follow is not one. Linkified escapes and
        // splits on the literal origin the signature copy hardcodes (the same
        // one FrontendUrl falls back to), so the newline-to-<br> pass can run
        // afterwards without touching the anchor it produced.
        var signatureHtml =
            Linkified(t.OutreachSignature, FrontendUrl.DefaultOrigin).Replace("\n", "<br>");

        // The same three answers the text body offers, in the same order — see
        // Answers. Rendered as ordinary body paragraphs rather than as buttons:
        // a second and third filled CTA would compete with the price button,
        // which is still the answer we want most.
        var answersHtml = string.Concat(Answers(t, copy.QuoteUrl).Select(a => $"""
                        <p style="margin:0 0 20px;color:{BodyText};font-size:15px;line-height:1.6;">{E(a)}</p>
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
                        <p style="margin:0 0 12px;color:{BodyText};font-size:16px;">{E(copy.Greeting)}</p>
                        <p style="margin:0 0 16px;color:{BodyText};font-size:15px;line-height:1.6;">{E(t.OutreachIntro)}</p>
                        <p style="margin:0 0 20px;color:{BodyText};font-size:15px;line-height:1.6;">{E(copy.Provenance)}</p>
            {urgentHtml}
                        <table width="100%" cellpadding="0" cellspacing="0" style="margin:0 0 20px;">
            {factRows}
                        </table>
                        <p style="margin:0 0 20px;color:{BodyText};font-size:15px;line-height:1.6;">{E(t.OutreachAsk)}</p>
            {ctaHtml}
            {answersHtml}
                        <p style="margin:0 0 24px;color:{BodyText};font-size:15px;line-height:1.6;">{Linkified(copy.Questions, copy.ContactUrl)}</p>
                        <p style="margin:0;color:{MutedText};font-size:14px;line-height:1.6;">{signatureHtml}</p>
                        <p style="margin:20px 0 0;padding-top:16px;border-top:1px solid #ECEFF1;color:{MutedText};font-size:12px;line-height:1.5;">{E(t.IntroOptOut(SupplierIntroComposer.OptOutKeyword))}</p>
                        <p style="margin:8px 0 0;color:{MutedText};font-size:12px;line-height:1.5;">{E(OperatorLegalLine)}</p>
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
