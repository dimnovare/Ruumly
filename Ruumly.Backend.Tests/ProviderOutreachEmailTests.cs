using System.Text.RegularExpressions;
using FluentAssertions;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Tests;

/// <summary>
/// The provider outreach email itself. A cold recipient must be able to answer
/// from a phone in ten seconds: who is asking, what the job is, how urgent it
/// is, and two ways to reply. The customer's identity never appears.
/// </summary>
public class ProviderOutreachEmailTests
{
    private static readonly string[] AllLanguages = ["et", "en", "ru", "lv", "lt"];

    private static DemandLead Lead(
        DateTime? needDate = null,
        string? details = "2-room flat, 3rd floor, no lift",
        string city = "Tallinn",
        string? toCity = "Tartu",
        string? query = null,
        DemandLeadCategory category = DemandLeadCategory.Moving) => new()
    {
        Id = Guid.NewGuid(),
        Email = "mari.maasikas@example.com",
        Name = "Mari Maasikas",
        Phone = "+372 5555 1234",
        City = city,
        ToCity = toCity,
        Category = category,
        NeedDate = needDate,
        Details = details,
        Query = query,
        Language = "et",
        Source = "concierge",
        Status = DemandLeadStatus.New,
        CreatedAt = DateTime.UtcNow,
    };

    private static Supplier Provider(string country = "EE", string name = "Kolimisabi OÜ") => new()
    {
        Id = Guid.NewGuid(), Name = name, ContactName = "C",
        ContactEmail = "provider@x.ee", ContactPhone = "1",
        Country = country, IsActive = true,
    };

    /// <summary>
    /// A sentence as it appears in the HTML body. Mirrors the composer's own
    /// minimal escaper — the English copy quotes "not possible" with straight
    /// double quotes, so asserting the raw string against the HTML body would
    /// fail for the right reason (it was escaped) or, in a NotContain, pass for
    /// the wrong one.
    /// </summary>
    private static string Html(string sentence) => sentence
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    // ─── Localization ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("et")]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("lv")]
    [InlineData("lt")]
    public void EveryLanguage_ProducesSubjectTextAndHtml(string language)
    {
        var message = ProviderOutreachComposer.ComposeInLanguage(
            language, Lead(), "https://ruumly.eu", OfferToken.Generate());

        message.Language.Should().Be(language);
        message.Subject.Should().NotBeNullOrWhiteSpace();
        message.TextBody.Should().NotBeNullOrWhiteSpace();
        message.HtmlBody.Should().NotBeNullOrWhiteSpace(
            "the delivery pipeline supports HTML end-to-end — outreach must not ship text-only");
        message.HtmlBody.Should().Contain("<html").And.Contain("</html>");
        // No external assets: no CDN CSS, no images, no tracking pixel.
        message.HtmlBody.Should().NotContain("<img").And.NotContain("<link").And.NotContain("<script");
    }

    [Fact]
    public void NoLanguageFallsBackToEnglishCopy()
    {
        var en = EmailTranslations.For("en");

        foreach (var language in AllLanguages.Where(l => l != "en"))
        {
            var t = EmailTranslations.For(language);
            var because = $"'{language}' outreach copy must be translated, not left on the English fallback";

            t.OutreachIntro.Should().NotBe(en.OutreachIntro, because);
            t.OutreachAsk.Should().NotBe(en.OutreachAsk, because);
            t.OutreachDateAsap.Should().NotBe(en.OutreachDateAsap, because);
            t.OutreachDetailsMissing.Should().NotBe(en.OutreachDetailsMissing, because);
            t.OutreachUrgentBadge.Should().NotBe(en.OutreachUrgentBadge, because);
            t.OutreachUrgentTpl.Should().NotBe(en.OutreachUrgentTpl, because);
            t.OutreachReplyAlternative.Should().NotBe(en.OutreachReplyAlternative, because);
            t.OutreachQuestionsTpl.Should().NotBe(en.OutreachQuestionsTpl, because);
            t.OutreachPackingAddOn.Should().NotBe(en.OutreachPackingAddOn, because);
            t.OutreachSubjectTpl.Should().NotBe(en.OutreachSubjectTpl, because);
            // The blocks a cold recipient reads before deciding whether to answer:
            // who is writing, why them, when the request arrived, and what the
            // lighter answers are. A silent English fallback in any of them is the
            // exact credibility leak the localized fact lines exist to prevent.
            t.OutreachGreetingTpl.Should().NotBe(en.OutreachGreetingTpl, because);
            t.OutreachProvenanceTpl.Should().NotBe(en.OutreachProvenanceTpl, because);
            t.OutreachCannotPrice.Should().NotBe(en.OutreachCannotPrice, because);
            // Shared with the introduction campaign, and now rendered into THIS
            // letter — so this test has to cover them from here too.
            t.IntroIfNotSuitable.Should().NotBe(en.IntroIfNotSuitable, because);
            t.IntroOptOutTpl.Should().NotBe(en.IntroOptOutTpl, because);
        }
    }

    /// <summary>
    /// The whole rendered letter, not just the strings behind it. A field left
    /// out of one of the five language literals would still compile and still
    /// pass a per-string comparison if the caller never printed it — this asserts
    /// that what actually reaches the provider is in their own language.
    /// </summary>
    [Theory]
    [InlineData("et")]
    [InlineData("ru")]
    [InlineData("lv")]
    [InlineData("lt")]
    public void NoRenderedBody_ContainsEnglishOutreachCopy(string language)
    {
        var en = EmailTranslations.For("en");
        var message = ProviderOutreachComposer.ComposeInLanguage(
            language, Lead(), "https://ruumly.eu", OfferToken.Generate(), "Kolimisabi OÜ");

        var english = new[]
        {
            en.OutreachIntro, en.OutreachAsk, en.OutreachCannotPrice,
            en.OutreachReplyAlternative, en.IntroIfNotSuitable,
            en.OutreachProvenance("2026-08-19"),
            en.OutreachGreetingTo("Kolimisabi OÜ"),
            en.IntroOptOut("REMOVE"),
        };

        foreach (var sentence in english)
        {
            var because = $"'{language}' outreach must not fall back to English";
            message.TextBody.Should().NotContain(sentence, because);
            message.HtmlBody.Should().NotContain(Html(sentence), because);
        }
    }

    [Theory]
    [InlineData("et")]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("lv")]
    [InlineData("lt")]
    public void EveryLanguage_HasNonEmptyOutreachStrings(string language)
    {
        var t = EmailTranslations.For(language);

        new[]
        {
            t.OutreachGreeting, t.OutreachGreetingTpl,
            t.OutreachIntro, t.OutreachProvenanceTpl, t.OutreachAsk,
            t.OutreachLabelService, t.OutreachLabelLocation,
            t.OutreachLabelDate, t.OutreachLabelDetails,
            t.OutreachDateAsap, t.OutreachDetailsMissing,
            t.OutreachPackingAddOn, t.OutreachCannotPrice,
            t.OutreachUrgentBadge, t.OutreachUrgentTpl,
            t.OutreachQuoteCta, t.OutreachReplyAlternative,
            t.OutreachSignature, t.OutreachQuestionsTpl,
            t.IntroIfNotSuitable, t.IntroOptOutTpl,
        }.Should().AllSatisfy(s => s.Should().NotBeNullOrWhiteSpace());
    }

    /// <summary>
    /// No "{company}" / "{date}" / "{keyword}" survives into a rendered body.
    ///
    /// The letter now carries five interpolated templates instead of two, each
    /// filled by a different Replace call in a different helper. A placeholder
    /// that reached a provider would not throw and would not fail any per-string
    /// test — it would simply be the single most damning thing a cold recipient
    /// could see, in the mail whose whole problem is looking machine-generated.
    /// </summary>
    [Theory]
    [InlineData("et")]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("lv")]
    [InlineData("lt")]
    public void NoRenderedOutput_LeavesATemplatePlaceholderUnfilled(string language)
    {
        var lead = Lead(needDate: DateTime.UtcNow.Date.AddDays(1), query: PackingQuery);
        lead.ScopeJson     = """{"movingSize":2}""";
        lead.PhotoKeysJson = """["a.jpg"]""";

        var message = ProviderOutreachComposer.ComposeInLanguage(
            language, lead, "https://ruumly.eu", OfferToken.Generate(), "Kolimisabi OÜ");

        // The HTML body is a raw string literal full of CSS braces, so it is
        // matched for the "{word}" shape only — which is what our own templates
        // use and what inline styles never produce.
        foreach (var rendered in new[] { message.Subject, message.TextBody, message.HtmlBody! })
        {
            Regex.IsMatch(rendered, @"\{[a-zA-Z]+\}").Should().BeFalse(
                $"'{language}' left an unfilled placeholder: {rendered}");
        }
    }

    [Fact]
    public void SupplierCountry_PicksTheProvidersLanguage()
    {
        ProviderOutreachComposer.Compose(Lead(), Provider("EE")).Language.Should().Be("et");
        ProviderOutreachComposer.Compose(Lead(), Provider("LV")).Language.Should().Be("lv");
        ProviderOutreachComposer.Compose(Lead(), Provider("LT")).Language.Should().Be("lt");
        ProviderOutreachComposer.Compose(Lead(), Provider("FI")).Language.Should().Be("en");
    }

    // ─── Privacy ──────────────────────────────────────────────────────────────

    // Both shapes: the packing add-on adds a fact line to the same table, so it
    // must not become a new place for customer identity to leak.
    [Theory]
    [InlineData(null)]
    [InlineData("concierge: moving +packing-addon | Tallinn→Tartu")]
    public void NeverLeaksTheCustomersNameEmailOrPhone(string? query)
    {
        var lead    = Lead(query: query);
        var message = ProviderOutreachComposer.Compose(
            lead, Provider(), "https://ruumly.eu", OfferToken.Generate());

        foreach (var secret in new[] { lead.Email, lead.Name!, lead.Phone! })
        {
            message.Subject.Should().NotContain(secret);
            message.TextBody.Should().NotContain(secret);
            message.HtmlBody.Should().NotContain(secret);
        }
    }

    /// <summary>
    /// Same promise, every language and every block. The letter grew a greeting,
    /// a provenance line and three answer paragraphs; each is one more template
    /// that could interpolate the wrong field, and the guarantee that a provider
    /// never learns who the customer is has to hold in all five renderings, not
    /// only the Estonian one the older test happened to exercise.
    /// </summary>
    [Theory]
    [InlineData("et")]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("lv")]
    [InlineData("lt")]
    public void NoLanguage_LeaksTheCustomersNameEmailOrPhone(string language)
    {
        var lead = Lead(query: "concierge: moving+warehouse +packing-addon | Tallinn→Tartu");
        lead.ScopeJson = """{"movingSize":2,"movingAccessFrom":3}""";
        lead.PhotoKeysJson = """["a.jpg","b.jpg"]""";

        var message = ProviderOutreachComposer.ComposeInLanguage(
            language, lead, "https://ruumly.eu", OfferToken.Generate(), "Kolimisabi OÜ");

        foreach (var secret in new[] { lead.Email, lead.Name!, lead.Phone!, "Maasikas", "5555" })
        foreach (var rendered in new[] { message.Subject, message.TextBody, message.HtmlBody! })
            rendered.Should().NotContain(secret);
    }

    [Fact]
    public void HtmlEncodesCustomerFreeText()
    {
        // Concierge intake does not reject angle brackets, so the details field
        // is untrusted input reaching an HTML body.
        var lead = Lead(details: "<script>alert('x')</script> & a \"quote\"");

        var message = ProviderOutreachComposer.Compose(lead, Provider());

        message.HtmlBody.Should().NotContain("<script>");
        message.HtmlBody.Should().Contain("&lt;script&gt;");
        message.TextBody.Should().Contain("<script>", "plain text needs no escaping");
    }

    // ─── Missing information ──────────────────────────────────────────────────

    [Fact]
    public void NoNeedDate_AsksForAsapInsteadOfPrintingADash()
    {
        var t = EmailTranslations.For("et");

        var message = ProviderOutreachComposer.Compose(Lead(needDate: null), Provider());

        message.TextBody.Should().Contain(t.OutreachDateAsap);
        message.TextBody.Should().NotContain($"{t.OutreachLabelDate}: —",
            "'Soovitud aeg: —' tells a provider nothing and reads as a broken template");
        message.Subject.Should().NotContain(t.OutreachUrgentBadge,
            "no date is not the same as an urgent date");
    }

    [Fact]
    public void NoDetails_SaysNotSpecifiedInsteadOfPrintingADash()
    {
        var t = EmailTranslations.For("et");

        var message = ProviderOutreachComposer.Compose(Lead(details: "   "), Provider());

        message.TextBody.Should().Contain(t.OutreachDetailsMissing);
        message.TextBody.Should().NotContain($"{t.OutreachLabelDetails}: —");
    }

    // ─── Packing add-on ───────────────────────────────────────────────────────
    // A "packing" concierge request is routed to a Moving lead, so the ask cannot
    // live in the lead's Category. It travels as a marker in the Query machine
    // summary and is rendered here as a localized fact line. It must NEVER travel
    // as English prose inside Details: that field is printed verbatim into a cold
    // email written in the PROVIDER's language, and one stray English sentence in
    // the middle of an Estonian mail is exactly what makes it read like spam.

    // What the concierge intake writes for a "packing" (or "moving"+"packing") request.
    private const string PackingQuery = "concierge: moving +packing-addon | Tallinn→Tartu";

    // The exact wording is hardcoded on purpose: asserting against
    // EmailTranslations would happily pass if a language quietly fell back to the
    // English copy, which is the whole bug this line exists to prevent.
    private const string PackingEstonian =
        "Klient soovib lisaks pakkimisabi — palun arvestage see oma hinna sisse.";
    private const string PackingLatvian =
        "Klients vēlas arī palīdzību ar iepakošanu — lūdzu, iekļaujiet to savā cenā.";

    [Theory]
    [InlineData("EE", PackingEstonian)]
    [InlineData("LV", PackingLatvian)]
    public void PackingAddOn_RendersInTheProvidersOwnLanguage(string country, string expected)
    {
        var english = EmailTranslations.For("en").OutreachPackingAddOn;

        var message = ProviderOutreachComposer.Compose(Lead(query: PackingQuery), Provider(country));

        message.TextBody.Should().Contain(expected);
        message.HtmlBody.Should().Contain(expected,
            "the composer produces both bodies and a provider may read either");
        foreach (var body in new[] { message.TextBody, message.HtmlBody })
        {
            body.Should().NotContain(english,
                "a silent English fallback is the exact credibility leak this replaces");
            body.Should().NotContain("[Packing help requested",
                "the old English bracket note must never reappear in provider mail");
        }
    }

    [Fact]
    public void NoPackingAddOn_GetsNoExtraLine()
    {
        var plain = ProviderOutreachComposer.Compose(
            Lead(query: "concierge: moving | Tallinn→Tartu | 2026-08-15"), Provider());

        plain.TextBody.Should().NotContain(PackingEstonian,
            "an ordinary moving lead must not be told the customer wants packing");
        plain.HtmlBody.Should().NotContain(PackingEstonian);

        // A lead that never carried a Query at all (older rows, other sources).
        var noQuery = ProviderOutreachComposer.Compose(Lead(), Provider());
        noQuery.TextBody.Should().NotContain(PackingEstonian);
    }

    [Fact]
    public void PackingMarker_IsOnlyHonouredInTheMachineWrittenPartOfQuery()
    {
        // Query is raw customer text on routed quote-request leads, and even a
        // concierge Query interpolates the customer's city. A visitor must not be
        // able to type the marker into a free-text field and inject a line into
        // provider mail.
        foreach (var forged in new[]
                 {
                     "I need help +packing-addon",
                     "concierge: moving | Tallinn +packing-addon",
                 })
        {
            ProviderOutreachComposer.Compose(Lead(query: forged), Provider())
                .TextBody.Should().NotContain(PackingEstonian, $"'{forged}' is customer-controlled text");
        }
    }

    // ─── Multi-service requests ───────────────────────────────────────────────
    // The intake invites the visitor to pick everything they need, and two
    // services do not fit one Category column — the lead lands on Any, whose
    // label is a deliberately generic "Teenus"/"Service". Sending that to a cold
    // provider asks them to price nothing at all. The selection survives in the
    // Query machine summary, so the ask is recovered and named, exactly like the
    // packing add-on above.

    // What the intake writes for a "moving"+"warehouse" pick.
    private const string MultiServiceQuery = "concierge: moving+warehouse | Tallinn→Tartu | 2026-08-15";

    [Fact]
    public void MultiServiceLead_NamesEveryServiceAsked_InTheProvidersLanguage()
    {
        var message = ProviderOutreachComposer.Compose(
            Lead(category: DemandLeadCategory.Any, query: MultiServiceQuery), Provider("EE"));

        // Hardcoded rather than read from EmailTranslations: asserting against the
        // table would pass just as happily if a language fell back to English.
        foreach (var body in new[] { message.TextBody, message.HtmlBody })
        {
            body.Should().Contain("Kolimine", "the mover must see the moving half of the job");
            body.Should().Contain("Laopind", "and the storage half they were also asked about");
        }

        message.Subject.Should().Contain("Kolimine").And.Contain("Laopind");
        message.Subject.Should().NotContain("Teenus",
            "'Service: Service' is what made an Any lead unanswerable");
    }

    [Fact]
    public void MultiServiceLead_InLatvian_UsesLatvianServiceNames()
    {
        var message = ProviderOutreachComposer.Compose(
            Lead(category: DemandLeadCategory.Any, query: MultiServiceQuery), Provider("LV"));

        message.TextBody.Should().Contain("Pārvākšanās").And.Contain("Noliktavas telpa");
        message.TextBody.Should().NotContain("Kolimine",
            "the provider's own country picks the language, not the customer's");
    }

    [Fact]
    public void AnyLead_WithNothingRecoverable_KeepsTheGenericLabel()
    {
        // An insurance-only ask, or any older row without a machine summary: there
        // is genuinely nothing to name, and the generic label is the honest answer.
        foreach (var query in new[] { null, "concierge: any +insurance-asked | Tallinn" })
        {
            ProviderOutreachComposer.Compose(
                    Lead(category: DemandLeadCategory.Any, query: query), Provider("EE"))
                .TextBody.Should().Contain("Teenus", $"'{query ?? "no query"}' names no service");
        }
    }

    [Fact]
    public void ServiceNames_AreOnlyReadFromTheMachineWrittenPartOfQuery()
    {
        // Same contract as the packing marker: Query is raw customer text on
        // routed leads and interpolates the customer's city on concierge ones, so
        // typing a service list into a free-text field must not rewrite the ask.
        foreach (var forged in new[]
                 {
                     "moving+warehouse",
                     "I need moving+warehouse help",
                     "concierge: any | Tallinn moving+warehouse",
                 })
        {
            ProviderOutreachComposer.Compose(
                    Lead(category: DemandLeadCategory.Any, query: forged), Provider("EE"))
                .TextBody.Should().Contain("Teenus", $"'{forged}' is customer-controlled text");
        }
    }

    // ─── Urgency ──────────────────────────────────────────────────────────────

    [Fact]
    public void NextDayNeedDate_IsFlaggedInTheSubjectAndAtTheTopOfTheBody()
    {
        var needDate = DateTime.UtcNow.Date.AddDays(1);
        var t        = EmailTranslations.For("et");

        var message = ProviderOutreachComposer.Compose(Lead(needDate: needDate), Provider());

        message.Subject.Should().StartWith(t.OutreachUrgentBadge,
            "a provider who only skims subject lines must still see next-day demand");
        message.TextBody.Should().Contain(t.OutreachUrgent(needDate.ToString("yyyy-MM-dd")));
        message.HtmlBody.Should().Contain(t.OutreachUrgent(needDate.ToString("yyyy-MM-dd")));
    }

    [Fact]
    public void DistantNeedDate_IsNotFlaggedUrgent()
    {
        var t = EmailTranslations.For("et");

        var message = ProviderOutreachComposer.Compose(
            Lead(needDate: DateTime.UtcNow.Date.AddDays(ProviderOutreachComposer.UrgentWithinDays + 1)),
            Provider());

        message.Subject.Should().NotContain(t.OutreachUrgentBadge);
        message.TextBody.Should().NotContain(t.OutreachUrgentBadge);
    }

    /// <summary>
    /// The urgency window is closed at BOTH ends.
    ///
    /// Outreach is re-composed every time an admin fans a lead out to another
    /// provider, which routinely happens days or weeks after it arrived — so a
    /// rule of "anything up to today + 3" eventually fires on a date that has
    /// already gone by, and the letter goes out shouting that a customer needs
    /// something by a date in the past. That is the one claim in the whole email
    /// a recipient can check against their own calendar, and getting it wrong
    /// proves the sender is a machine that does not read its own mail. The
    /// intake refuses past dates on the way in; only the passage of time can
    /// produce this, which is precisely why the composer has to catch it.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(-21)]
    [InlineData(-400)]
    public void NeedDateAlreadyPast_IsNotFlaggedUrgent(int daysAgo)
    {
        var t        = EmailTranslations.For("et");
        var needDate = DateTime.UtcNow.Date.AddDays(daysAgo);

        var message = ProviderOutreachComposer.Compose(Lead(needDate: needDate), Provider());

        message.Subject.Should().NotContain(t.OutreachUrgentBadge,
            "a date that has already passed is stale, not urgent");
        foreach (var body in new[] { message.TextBody, message.HtmlBody! })
        {
            body.Should().NotContain(t.OutreachUrgent(needDate.ToString("yyyy-MM-dd")));
            body.Should().NotContain(t.OutreachUrgentBadge);
        }

        // The date itself is still reported as the fact it is — suppressing the
        // badge must not suppress what the customer actually asked for.
        message.TextBody.Should().Contain(needDate.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void NeedDateToday_IsStillUrgent()
    {
        var today = DateTime.UtcNow.Date;

        var message = ProviderOutreachComposer.Compose(Lead(needDate: today), Provider());

        message.Subject.Should().StartWith(EmailTranslations.For("et").OutreachUrgentBadge,
            "the closed window must not have cut off today, which is the most urgent day there is");
    }

    // ─── Who the letter is addressed to ───────────────────────────────────────

    /// <summary>
    /// Eighteen Viljandi providers received eighteen letters opening "Tere!"
    /// about near-identical requests. The company name was in hand the whole
    /// time — Compose takes the Supplier row — and was read only for its country
    /// before being discarded.
    /// </summary>
    [Fact]
    public void Greeting_NamesTheRecipientsCompany()
    {
        var message = ProviderOutreachComposer.Compose(
            Lead(), Provider(name: "Lahe miniladu OÜ"), "https://ruumly.eu", OfferToken.Generate());

        foreach (var body in new[] { message.TextBody, message.HtmlBody! })
            body.Should().Contain("Tere, Lahe miniladu OÜ!");
    }

    [Fact]
    public void Greeting_IsWrittenInTheProvidersOwnLanguage()
    {
        // An Estonian-language lead, a Latvian supplier: the greeting follows the
        // recipient, like every other word in this letter.
        var message = ProviderOutreachComposer.Compose(Lead(), Provider("LV", "SIA Noliktava"));

        message.TextBody.Should().Contain("Sveiki, SIA Noliktava!");
        message.TextBody.Should().NotContain("Tere,");
    }

    /// <summary>
    /// A missing, blank or absurdly long name falls back to the plain greeting.
    /// Directory rows are imported from public sources: "Tere, !" or a greeting
    /// carrying half a legal description reads as a broken merge field, which is
    /// worse than not naming them at all.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Greeting_FallsBackWhenTheNameIsUnusable(string? name)
    {
        var t = EmailTranslations.For("et");

        var message = ProviderOutreachComposer.ComposeInLanguage(
            "et", Lead(), "https://ruumly.eu", OfferToken.Generate(), name);

        message.TextBody.Should().StartWith(t.OutreachGreeting);
        message.TextBody.Should().NotContain("Tere, !");
    }

    [Fact]
    public void Greeting_FallsBackWhenTheNameIsTooLongToPasteIntoASentence()
    {
        var t    = EmailTranslations.For("et");
        var huge = new string('A', 200);

        var message = ProviderOutreachComposer.ComposeInLanguage(
            "et", Lead(), "https://ruumly.eu", OfferToken.Generate(), huge);

        message.TextBody.Should().StartWith(t.OutreachGreeting);
        message.TextBody.Should().NotContain(huge);
    }

    [Fact]
    public void Greeting_EscapesTheCompanyNameInHtml()
    {
        // Supplier names arrive from a bulk directory import, not from a form we
        // control — the greeting is one more place raw text reaches an HTML body.
        var message = ProviderOutreachComposer.Compose(
            Lead(), Provider(name: "<b>Ladu</b> & Co \"OÜ\""), "https://ruumly.eu", OfferToken.Generate());

        message.HtmlBody.Should().NotContain("<b>Ladu</b>");
        message.HtmlBody.Should().Contain("&lt;b&gt;Ladu&lt;/b&gt; &amp; Co");
    }

    // ─── Calls to action ──────────────────────────────────────────────────────

    [Fact]
    public void OffersBothTheQuoteLinkAndAPlainReply()
    {
        var t     = EmailTranslations.For("et");
        var token = OfferToken.Generate();

        var message = ProviderOutreachComposer.Compose(
            Lead(), Provider(), "https://ruumly.eu", token);

        var link = $"https://ruumly.eu/et/quote/{token}";
        message.TextBody.Should().Contain(link);
        message.HtmlBody.Should().Contain($"href=\"{link}\"");
        message.TextBody.Should().Contain(t.OutreachReplyAlternative,
            "Reply-To is the ops inbox, so replying with a price already works");
        message.HtmlBody.Should().Contain(t.OutreachReplyAlternative);
        message.TextBody.Should().Contain(t.OutreachIntro,
            "a cold recipient has never heard of Ruumly — lead with the value proposition");
    }

    /// <summary>
    /// Three answers, not one.
    ///
    /// The letter used to ask for a price and offer no other way to respond, so
    /// a provider who could take the job but could not price it from what we
    /// sent — and a provider for whom the job was simply wrong — both had
    /// exactly one available action: nothing. Eighteen Viljandi contacts
    /// produced eighteen of them, and silence is also the only answer that
    /// teaches ops nothing.
    /// </summary>
    [Theory]
    [InlineData("et")]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("lv")]
    [InlineData("lt")]
    public void OffersAPrice_AQuestion_AndACheapDecline(string language)
    {
        var t = EmailTranslations.For(language);

        var message = ProviderOutreachComposer.ComposeInLanguage(
            language, Lead(), "https://ruumly.eu", OfferToken.Generate(), "Kolimisabi OÜ");

        message.TextBody.Should().Contain(t.OutreachQuoteCta, "a price is still the answer we want most");
        message.TextBody.Should().Contain(t.OutreachReplyAlternative, "replying with one works too");
        message.TextBody.Should().Contain(t.OutreachCannotPrice,
            "the need-info action has been on the quote page since 2026-08-18 and the email never mentioned it");
        message.TextBody.Should().Contain(t.IntroIfNotSuitable,
            "a provider who believes silence is the only alternative to a full quote will choose silence");

        message.HtmlBody.Should().Contain(Html(t.OutreachQuoteCta));
        message.HtmlBody.Should().Contain(Html(t.OutreachReplyAlternative));
        message.HtmlBody.Should().Contain(Html(t.OutreachCannotPrice));
        message.HtmlBody.Should().Contain(Html(t.IntroIfNotSuitable),
            "a provider reads whichever body their client renders — the answers cannot differ between them");
    }

    /// <summary>
    /// "Say on the same page what is missing" needs a page. With no token minted
    /// there is no quote page, and the sentence would point at nothing.
    /// </summary>
    [Fact]
    public void WithoutAQuoteToken_TheWhatIsMissingLineIsOmitted()
    {
        var t = EmailTranslations.For("et");

        var message = ProviderOutreachComposer.Compose(Lead(), Provider());

        message.TextBody.Should().NotContain(t.OutreachCannotPrice);
        message.HtmlBody.Should().NotContain(Html(t.OutreachCannotPrice));
        message.TextBody.Should().Contain(t.OutreachReplyAlternative,
            "the two channel-free answers survive without a token");
        message.TextBody.Should().Contain(t.IntroIfNotSuitable);
    }

    // ─── Earning the reply: provenance, opt-out, and who is writing ───────────

    /// <summary>
    /// The intro used to assert "this is a real customer request" — the one
    /// sentence a lead-generation bot would also write, and one the recipient
    /// cannot check. The submission date is evidence rather than assertion: it
    /// is specific, it sits beside the need date where it can be judged, and it
    /// is the customer's own row, not a claim about it.
    /// </summary>
    [Fact]
    public void SaysWhenTheCustomerActuallySubmittedTheRequest()
    {
        var t    = EmailTranslations.For("et");
        var lead = Lead(needDate: DateTime.UtcNow.Date.AddDays(30));
        lead.CreatedAt = new DateTime(2026, 7, 20, 9, 30, 0, DateTimeKind.Utc);

        var message = ProviderOutreachComposer.Compose(
            lead, Provider(), "https://ruumly.eu", OfferToken.Generate());

        foreach (var body in new[] { message.TextBody, message.HtmlBody! })
            body.Should().Contain(t.OutreachProvenance("2026-07-20"));

        message.TextBody.Should().NotContain("2026-07-20 —",
            "the submission date is prose, not another row in the fact table");
    }

    /// <summary>
    /// Every provider letter must be stoppable. This is the mail that arrives
    /// repeatedly and unbidden — one Viljandi provider was a candidate for four
    /// storage requests inside ten days — and it shipped with no way to opt out
    /// at all. A recipient who cannot find an unsubscribe uses the one their
    /// client gives them, and a spam complaint retires the address and costs
    /// sending reputation for every other provider too.
    /// </summary>
    [Theory]
    [InlineData("et")]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("lv")]
    [InlineData("lt")]
    public void CarriesAnOptOut_UsingTheSameKeywordAsTheIntroductionCampaign(string language)
    {
        var t = EmailTranslations.For(language);

        var message = ProviderOutreachComposer.ComposeInLanguage(
            language, Lead(), "https://ruumly.eu", OfferToken.Generate(), "Kolimisabi OÜ");

        var optOut = t.IntroOptOut(SupplierIntroComposer.OptOutKeyword);
        message.TextBody.Should().Contain(optOut);
        message.HtmlBody.Should().Contain(optOut);
        // One keyword across both letters, so a single inbox filter catches every
        // opt-out however the provider was reached.
        message.TextBody.Should().Contain("REMOVE");
    }

    /// <summary>
    /// A registry code is the one legitimacy signal in this letter that does not
    /// require believing us: an Estonian business owner can settle the question
    /// themselves in five seconds. It has to survive the no-images rule, so it is
    /// text in both bodies.
    /// </summary>
    [Theory]
    [InlineData("et")]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("lv")]
    [InlineData("lt")]
    public void NamesTheOperatingCompanyAndLinksTheSite(string language)
    {
        var message = ProviderOutreachComposer.ComposeInLanguage(
            language, Lead(), "https://ruumly.eu", OfferToken.Generate(), "Kolimisabi OÜ");

        foreach (var body in new[] { message.TextBody, message.HtmlBody! })
        {
            body.Should().Contain("Diip Solutions OÜ").And.Contain("17527757");
            body.Should().Contain("https://ruumly.eu");
        }

        message.HtmlBody.Should().Contain("href=\"https://ruumly.eu\"",
            "most clients do not auto-link a bare URL, and a signal nobody can follow is not one");
    }

    // ─── The subject line, read on a lock screen ──────────────────────────────

    /// <summary>
    /// Roughly thirty-five characters get read. The two facts that decide
    /// whether a provider opens this at all are "is this my area" and "is this
    /// what I do" — both used to sit behind "Ruumly — kliendipäring: ", and the
    /// city was last, so a Tallinn → Tartu move arrived with neither end of the
    /// route visible.
    /// </summary>
    [Fact]
    public void Subject_LeadsWithThePlaceAndTheService_NotTheBrand()
    {
        var subject = ProviderOutreachComposer.Compose(Lead(), Provider()).Subject;

        subject.Should().StartWith("Tallinn → Tartu",
            "the route decides whether this provider can serve the job at all");
        subject.Should().NotStartWith("Ruumly",
            "the brand is already the From display name; in the subject it is a wasted first line");
        subject[..35].Should().Contain("Kolimine", "the service has to survive truncation too");
    }

    [Fact]
    public void Subject_KeepsTheReferenceLast_SoTheHumanPartIsWhatSurvivesTruncation()
    {
        var lead    = Lead();
        var subject = ProviderOutreachComposer.Compose(lead, Provider()).Subject;

        subject.Should().EndWith($"[{ProviderOutreachComposer.Reference(lead.Id)}]");
    }

    // ─── Contact channels: reply or the contact page, never a phone ───────────

    /// <summary>
    /// The founder retired the support phone in 2026-08 — providers must reply
    /// to the mail or use the contact page, and nothing may invite a call.
    /// </summary>
    [Theory]
    [InlineData("et")]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("lv")]
    [InlineData("lt")]
    public void QuestionsLine_OffersReplyAndContactPage_AndNeverAPhone(string language)
    {
        var t          = EmailTranslations.For(language);
        var message    = ProviderOutreachComposer.ComposeInLanguage(
            language, Lead(), "https://ruumly.eu", OfferToken.Generate());
        var contactUrl = $"https://ruumly.eu/{language}/contact";

        message.TextBody.Should().Contain(t.OutreachQuestions(contactUrl));
        message.HtmlBody.Should().Contain($"href=\"{contactUrl}\"",
            "most email clients do not auto-link a bare URL in an HTML body");
        message.TextBody.Should().Contain(t.OutreachReplyAlternative,
            "replying is the other half of the offer");

        AssertNoPhoneAffordance(message.TextBody);
        AssertNoPhoneAffordance(message.HtmlBody!);
    }

    /// <summary>
    /// No AppUrl configured must still yield an absolute link: a relative
    /// "/et/contact" in an email body is a dead end.
    /// </summary>
    [Fact]
    public void ContactLink_IsAbsolute_EvenWithoutAnAppUrl()
    {
        var message = ProviderOutreachComposer.Compose(Lead(), Provider());

        message.TextBody.Should().Contain("https://ruumly.eu/et/contact");
        message.HtmlBody.Should().Contain("href=\"https://ruumly.eu/et/contact\"");
    }

    // ─── Lead reference (routing a provider's REPLY back to a request) ────────

    /// <summary>
    /// Replying is the primary action this email asks for, and Reply-To is one
    /// shared ops inbox. Without a reference the subject is only
    /// "{service}, {city → city}", so two live Tallinn → Tartu moving requests
    /// produce byte-identical subjects and an answer arrives with nothing on it
    /// saying which customer it is about.
    /// </summary>
    [Fact]
    public void Subject_CarriesTheLeadReference()
    {
        var lead    = Lead();
        var message = ProviderOutreachComposer.Compose(lead, Provider());

        message.Subject.Should().Contain(ProviderOutreachComposer.Reference(lead.Id));
    }

    [Fact]
    public void LeadReference_IsStableAndDistinguishesTwoIdenticalRequests()
    {
        var first  = Lead();
        var second = Lead();

        var a = ProviderOutreachComposer.Compose(first,  Provider()).Subject;
        var b = ProviderOutreachComposer.Compose(second, Provider()).Subject;

        a.Should().NotBe(b, "identical asks from two customers must be tellable apart");
        ProviderOutreachComposer.Reference(first.Id)
            .Should().Be(ProviderOutreachComposer.Reference(first.Id), "the reference is derived, not random");
    }

    [Theory]
    [InlineData("et")] [InlineData("en")] [InlineData("ru")]
    [InlineData("lv")] [InlineData("lt")]
    public void LeadReference_SurvivesEveryLanguageAndTheUrgentBadge(string language)
    {
        var lead = Lead(needDate: DateTime.UtcNow.Date.AddDays(1));
        var message = ProviderOutreachComposer.ComposeInLanguage(
            language, lead, "https://ruumly.eu", OfferToken.Generate());

        message.Subject.Should().Contain(ProviderOutreachComposer.Reference(lead.Id));
        message.Subject.Should().StartWith(EmailTranslations.For(language).OutreachUrgentBadge,
            "urgency still has to be the first thing a skimming provider sees");
    }

    /// <summary>
    /// The reference is a LOOKUP KEY, not a credential. It grants nothing on its
    /// own — unlike the per-recipient quote token, which is the actual bearer
    /// secret and must never appear beside it in a place the reference does.
    /// </summary>
    [Fact]
    public void LeadReference_IsNotDerivedFromTheQuoteToken()
    {
        var lead  = Lead();
        var token = OfferToken.Generate();
        var message = ProviderOutreachComposer.Compose(lead, Provider(), "https://ruumly.eu", token);

        message.Subject.Should().NotContain(token);
        ProviderOutreachComposer.Reference(lead.Id).Should().HaveLength(8);
    }

    internal static void AssertNoPhoneAffordance(string body)
    {
        body.Should().NotContain("tel:");
        body.Should().NotContain("5805 7795", "the founder's number was retired");
        body.Should().NotContainEquivalentOf("whatsapp");
        Regex.IsMatch(body, @"\+\d[\d\s()‑-]{6,}").Should().BeFalse(
            "no phone number may appear in supplier-facing mail");
    }
}
