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
        var (subject, body) = CustomerRequestAckComposer.Compose(
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
            var (_, body) = CustomerRequestAckComposer.Compose(
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
        var (_, body) = CustomerRequestAckComposer.Compose(
            Lead(name: null), "Kolimine", null);

        body.Should().StartWith(EmailTranslations.For("et").AckGreetingNoName);
        body.Should().NotContain("{name}");
    }

    [Fact]
    public void ShowsARouteWhenTheCustomerIsMovingBetweenCities()
    {
        var (_, body) = CustomerRequestAckComposer.Compose(
            Lead(city: "Tartu", toCity: "Tallinn"), "Kolimine", null);

        body.Should().Contain("Tartu → Tallinn");
    }

    [Fact]
    public void SaysAsSoonAsPossibleRatherThanPrintingNothingForAMissingDate()
    {
        var t = EmailTranslations.For("et");
        var (_, body) = CustomerRequestAckComposer.Compose(
            Lead(needDate: null), "Kolimine", null);

        body.Should().Contain(t.AckDateAsap);
    }

    [Fact]
    public void OmitsTheDetailsLineWhenTheCustomerGaveNone()
    {
        var t = EmailTranslations.For("et");
        var (_, body) = CustomerRequestAckComposer.Compose(
            Lead(details: null), "Kolimine", null);

        body.Should().NotContain(t.AckLabelDetails);
    }

    [Fact]
    public void LeavesNoUnreplacedPlaceholdersInAnyLanguage()
    {
        foreach (var language in AllLanguages)
        {
            var t = EmailTranslations.For(language);
            var (subject, body) = CustomerRequestAckComposer.Compose(
                Lead(language), t.CategoryLabel(DemandLeadCategory.Moving),
                "https://ruumly.eu/et/contact");

            (subject + body).Should().NotContain("{name}").And.NotContain("{url}");
        }
    }
}
