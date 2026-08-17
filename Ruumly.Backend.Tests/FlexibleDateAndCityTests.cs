using FluentAssertions;
using Ruumly.Backend.Constants;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Two things a real Haapsalu request exposed on 2026-08-17.
///
/// 1. "My date is flexible" was collected in the browser and never sent, so the
///    provider email told a mover the customer "gave no date, we'll confirm it"
///    when the customer had in fact answered the question. Silence and a
///    considered "any day suits me" are different facts, and only one of them is
///    quotable.
///
/// 2. The ops alert reported an unresolvable city as "no reachable provider
///    within 100 km" — which reads as missing supply and sends an operator off
///    to recruit partners. The real cause was "Haapsalu Lihula mnt 10" in the
///    city field while 34 movers sat within range of Haapsalu.
/// </summary>
public class FlexibleDateAndCityTests
{
    private static DemandLead Lead(string? query, DateTime? needDate = null) => new()
    {
        Id = Guid.NewGuid(), Email = "c@x.ee", Name = "Mari", Phone = "+372 5",
        City = "Haapsalu", Category = DemandLeadCategory.Moving,
        NeedDate = needDate, Query = query, Language = "et",
        Source = "concierge", Status = DemandLeadStatus.New, CreatedAt = DateTime.UtcNow,
    };

    private static Supplier Provider() => new()
    {
        Id = Guid.NewGuid(), Name = "Kolimisabi OÜ", ContactName = "C",
        ContactEmail = "p@x.ee", ContactPhone = "1", Country = "EE", IsActive = true,
    };

    // ─── The flexible-date marker ─────────────────────────────────────────────

    [Fact]
    public void FlexibleMarker_IsRecognisedOnlyInTheIntakesOwnSegment()
    {
        ServiceCategories.HasFlexibleDate(
            $"concierge: moving {ServiceCategories.DateFlexibleMarker} | Haapsalu").Should().BeTrue();

        // Raw customer text must never be read as a marker — routed quote leads
        // store the visitor's own message in Query.
        ServiceCategories.HasFlexibleDate(
            $"I need help, {ServiceCategories.DateFlexibleMarker}").Should().BeFalse();

        // Everything after the first " | " interpolates the customer's city, so a
        // visitor cannot inject the marker through a free-text field.
        ServiceCategories.HasFlexibleDate(
            $"concierge: moving | Haapsalu {ServiceCategories.DateFlexibleMarker}").Should().BeFalse();

        ServiceCategories.HasFlexibleDate("concierge: moving | Haapsalu").Should().BeFalse();
        ServiceCategories.HasFlexibleDate(null).Should().BeFalse();
    }

    [Theory]
    [InlineData("et")] [InlineData("en")] [InlineData("ru")]
    [InlineData("lv")] [InlineData("lt")]
    public void FlexibleDate_TellsTheProviderSomethingTheyCanActOn(string language)
    {
        var t = EmailTranslations.For(language);
        var message = ProviderOutreachComposer.ComposeInLanguage(
            language,
            Lead($"concierge: moving {ServiceCategories.DateFlexibleMarker} | Haapsalu"));

        message.TextBody.Should().Contain(t.OutreachDateFlexible);
        message.TextBody.Should().NotContain(t.OutreachDateAsap,
            "reporting an answered question as unanswered is the bug this fixes");
    }

    [Fact]
    public void NoDateAtAll_StillReportsTheHonestUnknown()
    {
        var t = EmailTranslations.For("et");
        var message = ProviderOutreachComposer.ComposeInLanguage(
            "et", Lead("concierge: moving | Haapsalu"));

        message.TextBody.Should().Contain(t.OutreachDateAsap);
        message.TextBody.Should().NotContain(t.OutreachDateFlexible);
    }

    [Fact]
    public void ANamedDateWinsOverBothPhrasings()
    {
        var t = EmailTranslations.For("et");
        // A flexible marker alongside a real date is contradictory input; the
        // date is the more specific fact and must win.
        var message = ProviderOutreachComposer.ComposeInLanguage(
            "et",
            Lead($"concierge: moving {ServiceCategories.DateFlexibleMarker} | Haapsalu",
                 needDate: DateTime.UtcNow.Date.AddDays(30)));

        message.TextBody.Should().NotContain(t.OutreachDateFlexible);
        message.TextBody.Should().NotContain(t.OutreachDateAsap);
    }

    [Theory]
    [InlineData("et")] [InlineData("en")] [InlineData("ru")]
    [InlineData("lv")] [InlineData("lt")]
    public void EveryLanguageDistinguishesTheTwoStates(string language)
    {
        var t = EmailTranslations.For(language);
        t.OutreachDateFlexible.Should().NotBeNullOrWhiteSpace();
        t.OutreachDateFlexible.Should().NotBe(t.OutreachDateAsap,
            $"{language} must say something different for an answered question");
    }

    // ─── The ops alert ────────────────────────────────────────────────────────

    [Fact]
    public void UnresolvedCity_IsReportedAsAnInputProblem_NotMissingSupply()
    {
        var line = AutoOutreachSummary
            .Skipped("city_unresolved", radiusKm: 100, context: "Haapsalu Lihula mnt 10")
            .Describe();

        line.Should().Contain("Haapsalu Lihula mnt 10", "the operator has to see the string that failed");
        line.Should().Contain("NOT missing supply");
        line.Should().NotContain("no reachable provider",
            "that phrasing sent operators off to recruit partners who already existed");
    }

    [Fact]
    public void GenuineNoSupply_StillReadsAsADistanceProblem()
    {
        var line = AutoOutreachSummary.Skipped("no_candidates", radiusKm: 100).Describe();

        line.Should().Contain("no reachable provider within 100 km");
        line.Should().NotContain("NOT missing supply");
    }

    [Fact]
    public void NoEmailCountStillSurfacesOnAGenuineMiss()
    {
        AutoOutreachSummary.Skipped("no_candidates", skippedNoEmail: 3, radiusKm: 50)
            .Describe().Should().Contain("3 skipped: no email");
    }
}
