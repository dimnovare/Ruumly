using FluentAssertions;
using Ruumly.Backend.Constants;

namespace Ruumly.Backend.Tests;

/// <summary>
/// The three-state truth table for a service slug.
///
/// A slug is in one of three states, and conflating any two of them has already
/// been shown to break something real:
///
///   BySlug          storage catalogue   - nothing is ever removed, so historical
///                                         DemandLead rows stay readable
///   BrowsableSlugs  directory catalogue - a visitor may browse it and an admin may
///                                         tag a supplier with it
///   ConsumerSlugs   sales catalogue     - intake, auto fan-out, "we bring you offers"
///
/// These tests exist because "trailer" is about to become the first slug that is
/// BROWSABLE BUT NOT SOLD, and the two obvious ways to express that are both wrong:
/// RetainedNotSoldSlugs additionally refuses the slug as a new admin service-type
/// (so the vertical could not grow), and the showTrailerService flag removes the
/// listings and sitemap hubs (so the directory presence is de-indexed).
///
/// Written BEFORE trailer moves, so they pin today's behaviour and fail loudly if
/// the move is made the wrong way.
/// </summary>
public class ServiceCategoriesTests
{
    // ── Set relationships ────────────────────────────────────────────────────

    [Fact]
    public void ConsumerSlugs_IsASubsetOf_BrowsableSlugs()
    {
        // Anything a visitor may BUY they must also be able to BROWSE. The reverse
        // does not hold, and that asymmetry is the whole point of the third state.
        ServiceCategories.ConsumerSlugs.Should().BeSubsetOf(ServiceCategories.BrowsableSlugs);
    }

    [Fact]
    public void BrowsableSlugs_IsASubsetOf_BySlug()
    {
        ServiceCategories.BrowsableSlugs.Should().BeSubsetOf(ServiceCategories.BySlug.Keys);
    }

    [Fact]
    public void RetainedSlugs_AreInTheStorageCatalogue_ButNeitherBrowsableNorSellable()
    {
        foreach (var slug in ServiceCategories.RetainedNotSoldSlugs)
        {
            ServiceCategories.BySlug.Should().ContainKey(slug,
                "removing it would make historical DemandLead rows unreadable");
            ServiceCategories.BrowsableSlugs.Should().NotContain(slug);
            ServiceCategories.ConsumerSlugs.Should().NotContain(slug);
            ServiceCategories.IsBrowsableSlug(slug).Should().BeFalse();
            ServiceCategories.IsConsumerSlug(slug).Should().BeFalse();
        }
    }

    [Fact]
    public void BrowsableNotSoldSlugs_AreBrowsable_ButNotSellable()
    {
        // Empty today. The assertion is the contract for whatever moves in.
        foreach (var slug in ServiceCategories.BrowsableNotSoldSlugs)
        {
            ServiceCategories.IsBrowsableSlug(slug).Should().BeTrue(
                "{0} must stay browsable - that is the entire reason for this set", slug);
            ServiceCategories.IsConsumerSlug(slug).Should().BeFalse();
            ServiceCategories.BrowsableSlugs.Should().Contain(slug);
            ServiceCategories.ConsumerSlugs.Should().NotContain(slug);
        }
    }

    // ── The alias split: sales side vs directory side ────────────────────────

    [Fact]
    public void DirectoryAliasFor_ResolvesEveryBrowsableSlug_ToItself()
    {
        // THE HIGHEST-VALUE ASSERTION HERE. LocationsController treats a null type
        // filter as "no filter", so a browsable slug that aliases to null would make
        // ?type={slug} answer with EVERY provider instead of that slug's. A de-sold
        // vertical must keep filtering exactly as it does today.
        foreach (var slug in ServiceCategories.BrowsableSlugs)
        {
            ServiceCategories.DirectoryAliasFor(slug).Should().Be(slug,
                "?type={0} must narrow the directory, never widen it", slug);
        }
    }

    [Fact]
    public void DirectoryAliasFor_StillRewritesRetainedSlugs()
    {
        ServiceCategories.DirectoryAliasFor(ServiceCategories.Packing)
            .Should().Be(ServiceCategories.Moving);
        ServiceCategories.DirectoryAliasFor(ServiceCategories.Insurance)
            .Should().BeNull();
    }

    [Fact]
    public void DirectoryAliasFor_UnknownSlug_IsNull()
    {
        ServiceCategories.DirectoryAliasFor("teleportation").Should().BeNull();
        ServiceCategories.DirectoryAliasFor(null).Should().BeNull();
    }

    [Fact]
    public void PublicAliasFor_AndDirectoryAliasFor_AgreeUntilSomethingIsDeSold()
    {
        // BrowsableNotSoldSlugs is empty, so the two functions are provably
        // equivalent right now. That equivalence is exactly why LocationsController
        // could be repointed safely ahead of the behaviour change. When this test
        // starts failing it means a slug was de-sold - which is expected, and the
        // failure is the reminder to check every PublicAliasFor caller for whether
        // it is really a SALES surface or a DIRECTORY one.
        foreach (var slug in ServiceCategories.BySlug.Keys)
        {
            if (ServiceCategories.BrowsableNotSoldSlugs.Contains(slug)) continue;
            ServiceCategories.DirectoryAliasFor(slug)
                .Should().Be(ServiceCategories.PublicAliasFor(slug), "slug {0}", slug);
        }
    }

    // ── Supply side: what an admin may put on a partner ──────────────────────

    [Fact]
    public void TryNormalizeOfferable_AcceptsTrailer_AsANewChoice()
    {
        // The inverse of the packing/insurance rule, and the guard against someone
        // "tidying" trailer into RetainedNotSoldSlugs. A trailer yard is a directory
        // row we intend to keep - and to keep ADDING.
        var ok = ServiceCategories.TryNormalizeOfferable(
            ["trailer"], alreadyStored: null, out var normalized, out var error);

        ok.Should().BeTrue(because: error ?? "trailer must remain addable by an admin");
        normalized.Should().Contain("trailer");
    }

    [Fact]
    public void TryNormalizeOfferable_TrailerOnly_IsNotAnEmptyOfferableList()
    {
        // A pure trailer yard is a legitimate partner, not a supplier that "sells
        // nothing". If this starts failing, the admin create/patch endpoints have
        // begun rejecting the very vertical the directory is keeping visible.
        var ok = ServiceCategories.TryNormalizeOfferable(
            ["trailer"], alreadyStored: [], out _, out var error);

        ok.Should().BeTrue(because: error ?? "a trailer-only partner must be creatable");
    }

    [Fact]
    public void TryNormalizeOfferable_StillRefusesRetainedSlugs_AsANewChoice()
    {
        var ok = ServiceCategories.TryNormalizeOfferable(
            [ServiceCategories.Packing], alreadyStored: null, out _, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
    }

    [Fact]
    public void TryNormalizeOfferable_PreservesARetainedSlugThatIsAlreadyStored()
    {
        // Retained means retained: an existing mover that also packs keeps its tag.
        var ok = ServiceCategories.TryNormalizeOfferable(
            ["moving"], alreadyStored: ["moving", ServiceCategories.Packing],
            out var normalized, out var error);

        ok.Should().BeTrue(because: error ?? "");
        normalized.Should().Contain(ServiceCategories.Packing);
    }
}
