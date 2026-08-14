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
    // Providers rarely type at all: the quote form gives them a dropdown, built
    // from the frontend's `priceUnit.*` keys. Those are ABBREVIATIONS, and the
    // table used to hold only full words — so the units the form itself hands us
    // were the ones most likely to reach the customer untranslated.
    [InlineData("/mēn", "et", "/kuu")]
    [InlineData("/mo", "ru", "/месяц")]
    [InlineData("/näd", "en", "/week")]
    [InlineData("/wk", "lv", "/nedēļā")]
    [InlineData("/ned", "en", "/week")]
    [InlineData("/sav", "en", "/week")]
    [InlineData("/hr", "et", "/tund")]
    [InlineData("/val", "et", "/tund")]
    [InlineData("/stunda", "en", "/hour")]
    [InlineData("/päev", "lv", "/diennaktī")]
    public void TranslatesTheAbbreviationsTheQuoteFormSubmits(string typed, string lang, string expected)
        => PriceUnitNormalizer.ToCustomerLanguage(typed, lang).Should().Be(expected);

    [Theory]
    // One-time is how a MOVE is priced — the flagship vertical — and it fell
    // through untranslated in all five languages. The form trims its dropdown
    // values before submitting; the raw key carries a leading space.
    [InlineData("one-time", "et", "ühekordne")]
    [InlineData(" one-time", "et", "ühekordne")]
    [InlineData("ühekordne", "en", "one-time")]
    [InlineData("разово", "lv", "vienreizējs")]
    [InlineData("vienreizējs", "lt", "vienkartinis")]
    [InlineData("vienkartinis", "ru", "разово")]
    // What a listing stores, carried verbatim onto an option an admin prefilled
    // from that listing.
    [InlineData("onetime", "et", "ühekordne")]
    public void TranslatesOneTime_TheUnitAMoveIsPricedIn(string typed, string lang, string expected)
        => PriceUnitNormalizer.ToCustomerLanguage(typed, lang).Should().Be(expected);

    [Theory]
    [InlineData("et")]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("lv")]
    [InlineData("lt")]
    public void OneTime_CarriesNoSlash_BecauseAFixedFeeIsNotARate(string lang)
        => PriceUnitNormalizer.ToCustomerLanguage("onetime", lang).Should().NotStartWith("/",
            "a fixed fee for the whole job is not a price per anything");

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
