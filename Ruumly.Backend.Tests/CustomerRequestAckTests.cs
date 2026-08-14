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

    private static DemandLead Lead(
        string language = "et",
        string? name = "Karlis",
        string city = "Rīga",
        string? toCity = null,
        DateTime? needDate = null,
        string? details = "Two rooms, one flight of stairs") => new()
    {
        Id = Guid.NewGuid(), Email = "customer@example.ee", Name = name,
        City = city, ToCity = toCity, Language = language,
        Category = DemandLeadCategory.Moving, NeedDate = needDate, Details = details,
    };

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
        foreach (var language in AllLanguages)
        {
            var t = EmailTranslations.For(language);
            var (_, body, _) = CustomerRequestAckComposer.Compose(
                Lead(language), t.CategoryLabel(DemandLeadCategory.Moving), null);

            body.Should().NotContain("24")
                .And.NotContainEquivalentOf("hour")
                .And.NotContainEquivalentOf("tund")
                .And.NotContainEquivalentOf("час")
                .And.NotContainEquivalentOf("stund")
                .And.NotContainEquivalentOf("valand");
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
        var message = CustomerRequestAckComposer.Compose(Lead("en"), "Moving", null);

        message.HtmlBody.Should().NotContainEquivalentOf("24 hour");
        message.HtmlBody.Should().NotContainEquivalentOf("2-3 offers");
    }

    [Fact]
    public void NoTrackingPixelsOrRemoteAssets()
    {
        var message = CustomerRequestAckComposer.Compose(Lead("et"), "Kolimine", null);

        message.HtmlBody.Should().NotContain("<img");
        message.HtmlBody.Should().NotContain("http://");
    }
}
