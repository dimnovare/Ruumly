using FluentAssertions;
using Ruumly.Backend.Helpers;
using Xunit;

namespace Ruumly.Backend.Tests;

/// <summary>
/// The HTML a customer receives is DERIVED from the operator's plain text, never
/// accepted as markup. These assert both halves of that bargain: the structure
/// people already type comes out looking like structure, and nothing that
/// arrives as text can leave as a tag.
/// </summary>
public class LeadMessageComposerTests
{
    [Fact]
    public void Paragraphs_SplitOnBlankLines()
    {
        var html = LeadMessageComposer.BuildHtml("Tere, Gerly!\n\nAus vahekokkuvõte.");

        html.Should().Contain("Tere, Gerly!");
        html.Should().Contain("Aus vahekokkuvõte.");
        // Two blocks, not one run-on paragraph.
        CountOf(html, "<p style=\"margin:0 0 16px").Should().Be(2);
    }

    [Fact]
    public void SingleNewlineInsideAParagraph_BecomesLineBreak_NotANewParagraph()
    {
        var html = LeadMessageComposer.BuildHtml("Esimene rida\nteine rida");

        CountOf(html, "<p style=\"margin:0 0 16px").Should().Be(1);
        html.Should().Contain("Esimene rida<br>teine rida");
    }

    [Fact]
    public void ShoutedLine_BecomesASectionHeading()
    {
        // Estonian Õ must behave like any other letter here.
        var html = LeadMessageComposer.BuildHtml("NEED ETTEVÕTTED ON PÄRINGU SAANUD\n\nTekst.");

        html.Should().Contain("letter-spacing:0.6px;\">NEED ETTEVÕTTED ON PÄRINGU SAANUD</p>");
    }

    [Fact]
    public void ALineWithNoLetters_IsNeverAHeading()
    {
        // "+372 5214653" uppercases to itself; only the letter test keeps it prose.
        LeadMessageComposer.BuildHtml("+372 5214653")
            .Should().NotContain("letter-spacing:0.6px;");
    }

    [Fact]
    public void BulletLines_BecomeARow_Each()
    {
        var html = LeadMessageComposer.BuildHtml("• Pargi Laod\n• Perevoz OÜ\n- Veotakso");

        CountOf(html, "&bull;").Should().Be(3);
        html.Should().Contain("Pargi Laod").And.Contain("Perevoz OÜ").And.Contain("Veotakso");
    }

    [Theory]
    [InlineData("Tel +372 5214653", "tel:+3725214653", "+372 5214653")]
    [InlineData("Tel +372 55599648.", "tel:+37255599648", "+372 55599648")]
    [InlineData("+37258587046", "tel:+37258587046", "+37258587046")]
    public void PhoneNumbers_BecomeDialableLinks_ShowingTheSpacedForm(
        string input, string href, string shown)
    {
        var html = LeadMessageComposer.BuildHtml(input);

        html.Should().Contain($"href=\"{href}\"");
        html.Should().Contain($">{shown}</a>");
    }

    [Fact]
    public void TrailingPunctuationIsNotDialled()
    {
        // A full stop after a number must stay prose, or the tel: link carries it.
        LeadMessageComposer.BuildHtml("Tel +372 6012957. Helistage.")
            .Should().Contain("href=\"tel:+3726012957\"")
            .And.Contain(". Helistage.");
    }

    [Fact]
    public void EmailsAndUrls_BecomeLinks()
    {
        var html = LeadMessageComposer.BuildHtml("info@ruumly.eu ja https://ruumly.eu/et");

        html.Should().Contain("href=\"mailto:info@ruumly.eu\"");
        html.Should().Contain("href=\"https://ruumly.eu/et\"");
    }

    // The endpoint rejects angle brackets before this is ever reached, so this is
    // the second lock on the same door — the composer is public and the next
    // caller may not have that guard.
    [Fact]
    public void Markup_InTheText_IsEscaped_NotRendered()
    {
        var html = LeadMessageComposer.BuildHtml("<script>alert(1)</script> & \"quoted\"");

        html.Should().NotContain("<script>");
        html.Should().Contain("&lt;script&gt;");
        html.Should().Contain("&amp;");
    }

    [Fact]
    public void PlainProse_WithNoneOfTheSyntax_StillRenders()
    {
        // Nothing an operator has already written needs rewriting.
        var html = LeadMessageComposer.BuildHtml("Tere! Saatsime teie päringu kuuele ettevõttele.");

        html.Should().Contain("Saatsime teie päringu kuuele ettevõttele.");
        html.Should().Contain("<!DOCTYPE html>");
    }

    [Fact]
    public void TheCardAndWordmark_AreAlwaysPresent()
    {
        var html = LeadMessageComposer.BuildHtml("Tere!");

        html.Should().Contain("#00897B");           // house teal
        html.Should().Contain(">Ruumly</td>");      // wordmark bar
        html.Should().Contain("max-width:560px");   // the card
    }

    private static int CountOf(string haystack, string needle)
    {
        var n = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
    }
}
