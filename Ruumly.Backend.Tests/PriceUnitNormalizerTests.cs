using FluentAssertions;
using Ruumly.Backend.Helpers;

namespace Ruumly.Backend.Tests;

/// <summary>
/// The provider quote form is rendered in the PROVIDER's language and the unit
/// is free text; the offer is read by the CUSTOMER. On 2026-08-13 a Rīga van
/// company answered an English-speaking customer with "/diena" and the offer
/// showed "60 € /diena" until it was corrected by hand. These pin the fix.
/// </summary>
public class PriceUnitNormalizerTests
{
    [Theory]
    // The exact case that shipped to a customer.
    [InlineData("/diena", "en", "/day")]
    [InlineData("/diena", "et", "/ööpäev")]
    [InlineData("/parai", "en", "/day")]
    [InlineData("/ööpäev", "lv", "/diennaktī")]
    [InlineData("/24h", "en", "/day")]
    [InlineData("/сутки", "en", "/day")]
    [InlineData("/tund", "en", "/hour")]
    [InlineData("/val.", "en", "/hour")]
    [InlineData("/stundā", "lt", "/val.")]
    [InlineData("/kuus", "en", "/month")]
    [InlineData("/mēnesī", "et", "/kuu")]
    [InlineData("/mėn.", "ru", "/месяц")]
    [InlineData("/nedēļā", "en", "/week")]
    public void TranslatesUnitsThatAreTheSameConcept(string typed, string lang, string expected)
        => PriceUnitNormalizer.ToCustomerLanguage(typed, lang).Should().Be(expected);

    [Theory]
    [InlineData("per day", "en", "/day")]
    [InlineData("24 h", "en", "/day")]
    [InlineData("day", "en", "/day")]
    [InlineData("  /DIENA  ", "en", "/day")]
    public void ToleratesHowProvidersActuallyType(string typed, string lang, string expected)
        => PriceUnitNormalizer.ToCustomerLanguage(typed, lang).Should().Be(expected);

    [Theory]
    // A provider who writes something we have no mapping for means it. Mangling
    // it would be worse than leaving it in their own words.
    [InlineData("/kuu esimesed 3 kuud")]
    [InlineData("/m³ kuus")]
    [InlineData("per pallet")]
    [InlineData("kokkuleppel")]
    public void LeavesAnythingItDoesNotRecogniseAlone(string typed)
        => PriceUnitNormalizer.ToCustomerLanguage(typed, "en").Should().Be(typed);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PassesBlanksStraightThrough(string? typed)
        => PriceUnitNormalizer.ToCustomerLanguage(typed, "en").Should().Be(typed);

    [Fact]
    public void FallsBackToEnglishForAnUnknownTargetLanguage()
        => PriceUnitNormalizer.ToCustomerLanguage("/diena", "fi").Should().Be("/day");
}
