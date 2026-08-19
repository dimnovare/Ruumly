using FluentAssertions;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Tests;

/// <summary>
/// The receipt a customer gets on submitting a request. Before 2026-08-13 there
/// was none: the only mail leaving the intake went to the ops inbox, and the
/// first thing a customer heard was the offer, days later.
/// </summary>
public class CustomerRequestAckTests
{
    private static readonly string[] AllLanguages = ["et", "en", "ru", "lv", "lt"];

    /// <summary>
    /// A token with no "24" and no digits that could be mistaken for a duration.
    /// The no-deadline assertions below scan the WHOLE body, the status link
    /// included, so a realistic random token would make them flaky for reasons
    /// that have nothing to do with the copy.
    /// </summary>
    private const string Token = "wKqZtRfBmXcLdNpVsHgJyEuAoIbTnMrQwZxCvBnMlKj";

    private static DemandLead Lead(
        string language = "et",
        string? name = "Karlis",
        string city = "Rīga",
        string? toCity = null,
        DateTime? needDate = null,
        string? details = "Two rooms, one flight of stairs",
        string? statusToken = null) => new()
    {
        Id = Guid.NewGuid(), Email = "customer@example.ee", Name = name,
        City = city, ToCity = toCity, Language = language,
        Category = DemandLeadCategory.Moving, NeedDate = needDate, Details = details,
        StatusToken = statusToken,
    };

    /// <summary>The link exactly as SupportController builds it.</summary>
    private static string StatusUrl(string language, string token = Token) =>
        FrontendUrl.Localized("https://ruumly.eu", language, $"request-status/{token}");

    [Theory]
    [InlineData("et")] [InlineData("en")] [InlineData("ru")]
    [InlineData("lv")] [InlineData("lt")]
    public void ReadsTheRequestBackInTheCustomersOwnLanguage(string language)
    {
        var t = EmailTranslations.For(language);
        var (subject, body, _) = CustomerRequestAckComposer.Compose(
            Lead(language, needDate: new DateTime(2026, 8, 20)),
            t.CategoryLabel(DemandLeadCategory.Moving),
            "https://ruumly.eu/et/contact");

        subject.Should().Be(t.AckSubject).And.NotBeNullOrWhiteSpace();
        // Reading the request back is the cheapest proof that something
        // human-shaped arrived, and it is where a customer spots their own typo.
        body.Should().Contain(t.AckSummaryHeading)
            .And.Contain(t.CategoryLabel(DemandLeadCategory.Moving))
            .And.Contain("Rīga")
            .And.Contain("20.08.2026")
            .And.Contain("Two rooms, one flight of stairs");
        body.Should().Contain(t.AckReply, "the reply thread is the point of the whole mail");
    }

    [Fact]
    public void NeverRepeatsTheTwentyFourHourPromise()
    {
        // Some requests reach no provider automatically — a multi-service ask is
        // routed by hand, and an unmatched city needs a human. Restating a
        // deadline in the one message that proves we received the request would
        // turn an honest wait into a broken promise.
        //
        // Checked with AND without the status link: pointing at a page that
        // reports the stage a request reached is not a forecast, and the copy
        // that introduces it must not smuggle one back in.
        foreach (var language in AllLanguages)
        {
            var t = EmailTranslations.For(language);
            foreach (var statusUrl in new[] { null, StatusUrl(language) })
            {
                // Text body only: the HTML carries "24" inside inline CSS
                // (margin:0 0 24px), so the bare-digit rule can only be enforced
                // on prose. HtmlStillRefusesToRepeatTheDeadlinePromise covers
                // the other body with the assertions that survive markup.
                var (_, body, _) = CustomerRequestAckComposer.Compose(
                    Lead(language, statusToken: Token),
                    t.CategoryLabel(DemandLeadCategory.Moving), null, statusUrl);

                body.Should().NotContain("24")
                    .And.NotContainEquivalentOf("hour")
                    .And.NotContainEquivalentOf("tund")
                    .And.NotContainEquivalentOf("час")
                    .And.NotContainEquivalentOf("stund")
                    .And.NotContainEquivalentOf("valand");
            }
        }
    }

    [Fact]
    public void HandlesAMissingNameWithoutGreetingNobody()
    {
        var (_, body, _) = CustomerRequestAckComposer.Compose(
            Lead(name: null), "Kolimine", null);

        body.Should().StartWith(EmailTranslations.For("et").AckGreetingNoName);
        body.Should().NotContain("{name}");
    }

    [Fact]
    public void ShowsARouteWhenTheCustomerIsMovingBetweenCities()
    {
        var (_, body, _) = CustomerRequestAckComposer.Compose(
            Lead(city: "Tartu", toCity: "Tallinn"), "Kolimine", null);

        body.Should().Contain("Tartu → Tallinn");
    }

    [Fact]
    public void SaysAsSoonAsPossibleRatherThanPrintingNothingForAMissingDate()
    {
        var t = EmailTranslations.For("et");
        var (_, body, _) = CustomerRequestAckComposer.Compose(
            Lead(needDate: null), "Kolimine", null);

        body.Should().Contain(t.AckDateAsap);
    }

    [Fact]
    public void OmitsTheDetailsLineWhenTheCustomerGaveNone()
    {
        var t = EmailTranslations.For("et");
        var (_, body, _) = CustomerRequestAckComposer.Compose(
            Lead(details: null), "Kolimine", null);

        body.Should().NotContain(t.AckLabelDetails);
    }

    [Fact]
    public void LeavesNoUnreplacedPlaceholdersInAnyLanguage()
    {
        foreach (var language in AllLanguages)
        {
            var t = EmailTranslations.For(language);
            var (subject, body, _) = CustomerRequestAckComposer.Compose(
                Lead(language), t.CategoryLabel(DemandLeadCategory.Moving),
                "https://ruumly.eu/et/contact");

            (subject + body).Should().NotContain("{name}").And.NotContain("{url}");
        }
    }

    // ─── HTML body ────────────────────────────────────────────────────────────
    //
    // The receipt was text-only while the COLD email we send to strangers was
    // branded HTML, so a customer's sole proof that Ruumly received their
    // request looked less legitimate than unsolicited mail.

    [Theory]
    [InlineData("et")] [InlineData("en")] [InlineData("ru")]
    [InlineData("lv")] [InlineData("lt")]
    public void EveryLanguage_RendersAWellFormedHtmlBody(string language)
    {
        var message = CustomerRequestAckComposer.Compose(
            Lead(language, needDate: new DateTime(2026, 9, 20)),
            "Kolimine", "https://ruumly.eu/et/contact");

        message.HtmlBody.Should().StartWith("<!DOCTYPE html>");
        message.HtmlBody.Should().Contain("</html>");
        message.HtmlBody.Should().Contain("Ruumly");
        message.TextBody.Should().NotBeNullOrWhiteSpace("the plain-text fallback must survive");
    }

    [Fact]
    public void HtmlReadsTheRequestBack_LikeTheTextVersion()
    {
        var message = CustomerRequestAckComposer.Compose(
            Lead("et", city: "Tallinn", toCity: "Tartu", needDate: new DateTime(2026, 9, 20)),
            "Kolimine", null);

        message.HtmlBody.Should().Contain("Kolimine");
        message.HtmlBody.Should().Contain("Tallinn");
        message.HtmlBody.Should().Contain("Tartu");
        message.HtmlBody.Should().Contain("20.09.2026");
    }

    /// <summary>
    /// The name and the details are raw customer input rendered straight into
    /// an HTML document. A visitor who types a tag must not be able to inject
    /// markup into the mail their own address receives.
    /// </summary>
    [Fact]
    public void CustomerSuppliedTextIsEscaped()
    {
        var message = CustomerRequestAckComposer.Compose(
            Lead("et", name: "<script>alert(1)</script>",
                 details: "3rd floor & \"no lift\" <b>urgent</b>"),
            "Kolimine", null);

        message.HtmlBody.Should().NotContain("<script>");
        message.HtmlBody.Should().Contain("&lt;script&gt;");
        message.HtmlBody.Should().Contain("&amp;");
        message.HtmlBody.Should().Contain("&quot;no lift&quot;");
        message.HtmlBody.Should().NotContain("<b>urgent</b>");
    }

    /// <summary>
    /// WebUtility.HtmlEncode would turn every non-ASCII character into a numeric
    /// entity. This message is read by Estonian, Latvian, Lithuanian and Russian
    /// customers looking at their OWN name — mangling it is the one thing a
    /// receipt cannot afford.
    /// </summary>
    [Fact]
    public void DiacriticsAndCyrillicSurviveIntact()
    {
        var message = CustomerRequestAckComposer.Compose(
            Lead("ru", name: "Пётр Õunapuu", city: "Kärdla",
                 details: "Ühistu värav, 3. korrus — ilma liftita"),
            "Перевозка", null);

        message.HtmlBody.Should().Contain("Пётр Õunapuu");
        message.HtmlBody.Should().Contain("Kärdla");
        message.HtmlBody.Should().Contain("ilma liftita");
        message.HtmlBody.Should().NotContain("&#");
    }

    [Fact]
    public void HtmlStillRefusesToRepeatTheDeadlinePromise()
    {
        // Same contract as the text body: some requests reach no provider
        // automatically, so the one message proving we received the request
        // must not restate a deadline nobody enforces.
        var message = CustomerRequestAckComposer.Compose(
            Lead("en", statusToken: Token), "Moving", null, StatusUrl("en"));

        message.HtmlBody.Should().NotContainEquivalentOf("24 hour");
        message.HtmlBody.Should().NotContainEquivalentOf("2-3 offers");
    }

    // ─── The customer's own status page ───────────────────────────────────────
    //
    // /{lang}/request-status/{token} shipped with nothing linking to it: the
    // token was minted on every concierge lead and never told to anybody, so
    // the page built to end the silence between receipt and offer could not be
    // reached by the people waiting in it. This mail is the durable half of the
    // fix — the success screen is seen once, a receipt is kept.

    [Theory]
    [InlineData("et")] [InlineData("en")] [InlineData("ru")]
    [InlineData("lv")] [InlineData("lt")]
    public void CarriesTheCustomersOwnStatusLink_InTheirOwnLanguage(string language)
    {
        var t   = EmailTranslations.For(language);
        var url = StatusUrl(language);

        var message = CustomerRequestAckComposer.Compose(
            Lead(language, statusToken: Token),
            t.CategoryLabel(DemandLeadCategory.Moving),
            FrontendUrl.Contact("https://ruumly.eu", language),
            url);

        // The token is THIS lead's, and the language segment is the one the
        // customer filled the form in — a receipt that opened the status page in
        // Estonian for a Russian-speaking customer would be a worse silence.
        url.Should().Contain($"/{language}/request-status/{Token}");

        // A plain-text client shows the whole URL; an HTML client shows a
        // button. Both must actually point somewhere.
        message.TextBody.Should().Contain(url).And.Contain(t.AckStatusCta);
        message.HtmlBody.Should().Contain($"href=\"{url}\"").And.Contain(t.AckStatusCta);

        // The sentence explaining what the link is, not just a naked URL.
        message.TextBody.Should().Contain(t.AckStatusLine);
        message.HtmlBody.Should().Contain(t.AckStatusLine);
    }

    [Fact]
    public void EveryLanguageHasItsOwnStatusCopy_NotTheEnglishFallback()
    {
        var en = EmailTranslations.For("en");
        foreach (var language in AllLanguages.Where(l => l != "en"))
        {
            var t = EmailTranslations.For(language);
            t.AckStatusLine.Should().NotBeNullOrWhiteSpace()
                .And.NotBe(en.AckStatusLine, $"'{language}' needs its own status copy");
            t.AckStatusCta.Should().NotBeNullOrWhiteSpace()
                .And.NotBe(en.AckStatusCta, $"'{language}' needs its own status CTA");
        }
    }

    /// <summary>
    /// A lead with no token — every row created before the column existed, and
    /// any future path that forgets to mint one — must compose the receipt it
    /// composed before this feature, not one carrying a link that 404s the
    /// person we were trying to reassure.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ALeadWithNoToken_GetsNoLinkAtAll_RatherThanADeadOne(string? statusUrl)
    {
        var t       = EmailTranslations.For("et");
        var contact = FrontendUrl.Contact("https://ruumly.eu", "et");
        var lead    = Lead("et", needDate: new DateTime(2026, 9, 20));

        var message = CustomerRequestAckComposer.Compose(lead, "Kolimine", contact, statusUrl);
        // An empty or whitespace URL has to behave like no argument at all —
        // SupportController builds the string, and a lead whose token went
        // missing would otherwise produce "" here rather than null.
        var omitted = CustomerRequestAckComposer.Compose(lead, "Kolimine", contact);

        message.TextBody.Should().Be(omitted.TextBody);
        message.HtmlBody.Should().Be(omitted.HtmlBody);

        foreach (var body in new[] { message.TextBody, message.HtmlBody })
            body.Should().NotContain("request-status")
                .And.NotContain(t.AckStatusCta)
                .And.NotContain(t.AckStatusLine)
                .And.NotContain("href=\"\"");

        // And the mail is still the whole mail, not a truncated one.
        message.TextBody.Should().Contain(t.AckSummaryHeading).And.Contain(t.AckReply);
    }

    /// <summary>
    /// The status link is a bearer credential in a URL. It is built by the
    /// caller, but it still lands in an attribute in a document this class
    /// generates, so it goes through the same escaper as everything else.
    /// </summary>
    [Fact]
    public void TheStatusLinkIsEscapedLikeEveryOtherInterpolatedValue()
    {
        var message = CustomerRequestAckComposer.Compose(
            Lead("et", statusToken: Token), "Kolimine", null,
            "https://ruumly.eu/et/request-status/tok\"><script>alert(1)</script>");

        message.HtmlBody.Should().NotContain("<script>");
        message.HtmlBody.Should().Contain("&quot;&gt;&lt;script&gt;");
    }

    [Fact]
    public void NoTrackingPixelsOrRemoteAssets()
    {
        var message = CustomerRequestAckComposer.Compose(Lead("et"), "Kolimine", null);

        message.HtmlBody.Should().NotContain("<img");
        message.HtmlBody.Should().NotContain("http://");
    }
}
