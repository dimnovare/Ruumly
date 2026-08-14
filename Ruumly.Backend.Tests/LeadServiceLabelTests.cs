using FluentAssertions;
using Ruumly.Backend.Constants;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Naming what a lead is a request FOR, for every audience that reads it back.
///
/// The bug this locks down: a lead carries ONE Category column while the intake
/// copy explicitly invites the visitor to pick several services, so a
/// multi-service request collapses to <c>Any</c> — whose label is the generic
/// "Service". The provider cold email already recovered the real list from the
/// Query machine summary; the CUSTOMER'S OWN RECEIPT did not. The stranger we
/// cold-emailed got a better description of the request than the person who
/// made it, in the one message whose stated job is to let them spot their own
/// typo.
/// </summary>
public class LeadServiceLabelTests
{
    private static readonly string[] AllLanguages = ["et", "en", "ru", "lv", "lt"];

    private static DemandLead MultiService(string query) => new()
    {
        Id = Guid.NewGuid(), Email = "c@x.ee", City = "Tallinn",
        Category = DemandLeadCategory.Any, Query = query, Language = "et",
    };

    [Theory]
    [InlineData("et")] [InlineData("en")] [InlineData("ru")]
    [InlineData("lv")] [InlineData("lt")]
    public void MultiServiceLead_NamesEveryServiceRequested(string language)
    {
        var t    = EmailTranslations.For(language);
        var lead = MultiService("concierge: moving+warehouse | Tallinn→Tartu | 2026-09-01");

        var label = LeadServiceLabel.For(t, lead);

        label.Should().Contain(t.CategoryLabel(DemandLeadCategory.Moving));
        label.Should().Contain(t.CategoryLabel(DemandLeadCategory.Warehouse));
        label.Should().NotBe(t.CategoryLabel(DemandLeadCategory.Any),
            "the generic label is exactly what makes a multi-service receipt useless");
    }

    [Fact]
    public void SingleCategoryLead_UsesItsOwnLabel()
    {
        var lead = new DemandLead
        {
            Id = Guid.NewGuid(), Email = "c@x.ee", City = "Tartu",
            Category = DemandLeadCategory.Cleaning, Language = "et",
        };

        LeadServiceLabel.For(EmailTranslations.For("et"), lead)
            .Should().Be(EmailTranslations.For("et").CategoryLabel(DemandLeadCategory.Cleaning));
    }

    [Fact]
    public void UnrecoverableLead_FallsBackToTheGenericLabel()
    {
        // A lead whose Query is raw customer text (SupportController's routed
        // quote leads store the visitor's message there) must not have services
        // read out of it.
        var lead = MultiService("I need help with something, call me");

        LeadServiceLabel.For(EmailTranslations.For("en"), lead)
            .Should().Be(EmailTranslations.For("en").CategoryLabel(DemandLeadCategory.Any));
    }

    [Fact]
    public void VisitorCannotInjectServicesThroughFreeText()
    {
        // Same guarantee ServiceCategories.SelectedSlugs makes: only a Query
        // carrying the concierge prefix, and only its leading segment, is read.
        // Otherwise a visitor could steer who gets emailed by typing into a form.
        var lead = MultiService("concierge: cleaning | Tallinn+moving+warehouse | 2026-09-01");

        var label = LeadServiceLabel.For(EmailTranslations.For("en"), lead);

        label.Should().Be(EmailTranslations.For("en").CategoryLabel(DemandLeadCategory.Cleaning));
    }

    [Fact]
    public void RetiredSlugsNeverSurfaceAsAService()
    {
        // packing/insurance are retained for data, not for sale — they must not
        // be named to a customer or a provider as something we sell.
        var lead = MultiService(
            $"concierge: moving {ServiceCategories.PackingAddOnMarker} | Tallinn | 2026-09-01");

        var label = LeadServiceLabel.For(EmailTranslations.For("en"), lead);

        label.Should().Be(EmailTranslations.For("en").CategoryLabel(DemandLeadCategory.Moving));
    }

    [Fact]
    public void EveryLanguageProducesANonEmptyLabel()
    {
        var lead = MultiService("concierge: cleaning+vanrental | Tallinn | 2026-09-01");

        foreach (var language in AllLanguages)
            LeadServiceLabel.For(EmailTranslations.For(language), lead)
                .Should().NotBeNullOrWhiteSpace($"language {language} must resolve service labels");
    }
}
