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
        string? query = null) => new()
    {
        Id = Guid.NewGuid(),
        Email = "mari.maasikas@example.com",
        Name = "Mari Maasikas",
        Phone = "+372 5555 1234",
        City = city,
        ToCity = toCity,
        Category = DemandLeadCategory.Moving,
        NeedDate = needDate,
        Details = details,
        Query = query,
        Language = "et",
        Source = "concierge",
        Status = DemandLeadStatus.New,
        CreatedAt = DateTime.UtcNow,
    };

    private static Supplier Provider(string country = "EE") => new()
    {
        Id = Guid.NewGuid(), Name = "Kolimisabi OÜ", ContactName = "C",
        ContactEmail = "provider@x.ee", ContactPhone = "1",
        Country = country, IsActive = true,
    };

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
            language, Lead(), "https://ruumly.eu", OfferToken.Generate(), opsPhone: "+372 5555 0000");

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
            t.OutreachPackingAddOn.Should().NotBe(en.OutreachPackingAddOn, because);
            t.OutreachSubjectTpl.Should().NotBe(en.OutreachSubjectTpl, because);
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
            t.OutreachGreeting, t.OutreachIntro, t.OutreachAsk,
            t.OutreachLabelService, t.OutreachLabelLocation,
            t.OutreachLabelDate, t.OutreachLabelDetails,
            t.OutreachDateAsap, t.OutreachDetailsMissing,
            t.OutreachPackingAddOn,
            t.OutreachUrgentBadge, t.OutreachUrgentTpl,
            t.OutreachQuoteCta, t.OutreachReplyAlternative,
            t.OutreachSignature, t.OutreachPhoneLabel,
        }.Should().AllSatisfy(s => s.Should().NotBeNullOrWhiteSpace());
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
            lead, Provider(), "https://ruumly.eu", OfferToken.Generate(), opsPhone: "+372 5555 0000");

        foreach (var secret in new[] { lead.Email, lead.Name!, lead.Phone! })
        {
            message.Subject.Should().NotContain(secret);
            message.TextBody.Should().NotContain(secret);
            message.HtmlBody.Should().NotContain(secret);
        }
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

    // ─── Support phone (PlatformSettings opsPhone) ────────────────────────────

    [Fact]
    public void PhoneLine_IsOmittedWhenUnset_AndRenderedWhenSet()
    {
        var t = EmailTranslations.For("et");

        foreach (var unset in new[] { null, "", "   " })
        {
            var message = ProviderOutreachComposer.Compose(Lead(), Provider(), opsPhone: unset);
            message.TextBody.Should().NotContain($"{t.OutreachPhoneLabel}:",
                "an empty or placeholder phone must never be rendered");
            message.HtmlBody.Should().NotContain($"{t.OutreachPhoneLabel}:");
        }

        var withPhone = ProviderOutreachComposer.Compose(
            Lead(), Provider(), opsPhone: " +372 5555 0000 ");
        withPhone.TextBody.Should().Contain($"{t.OutreachPhoneLabel}: +372 5555 0000");
        withPhone.HtmlBody.Should().Contain("+372 5555 0000");
    }
}
