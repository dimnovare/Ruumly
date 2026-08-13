using FluentAssertions;
using Ruumly.Backend.Helpers;

namespace Ruumly.Backend.Tests;

/// <summary>
/// City matching decides whether the auto fan-out emails anyone at all: the
/// geographic anchor is built only from rows that match, and with no anchor the
/// nearby search returns nothing. Every case below was checked against the
/// production directory on 2026-08-13 — the "used to return zero" ones really
/// did return zero for real customers.
/// </summary>
public class CityMatcherTests
{
    [Theory]
    // Ruumly's OWN Russian intake placeholder. A Russian-speaking Tallinn
    // customer who typed what the form suggested reached no providers.
    [InlineData("Таллинн", "Tallinn")]
    [InlineData("Таллин", "Tallinn")]
    [InlineData("Тарту", "Tartu")]
    [InlineData("Вильнюс", "Vilnius")]
    [InlineData("Рига", "Rīga")]
    // The intake hint literally reads "We mainly operate in: Tallinn, Harjumaa".
    [InlineData("Harjumaa", "Tallinn")]
    [InlineData("Harju maakond", "Tallinn")]
    // Districts people give instead of the city.
    [InlineData("Lasnamäe", "Tallinn")]
    [InlineData("Mustamäe", "Tallinn")]
    [InlineData("Nõmme", "Tallinn")]
    // Diacritics typed, or not typed, either way round.
    [InlineData("Riga", "Rīga")]
    [InlineData("Rīga", "Riga")]
    [InlineData("Parnu", "Pärnu")]
    [InlineData("Klaipeda", "Klaipėda")]
    [InlineData("Klaipėdos r.", "Klaipeda")]
    // Case and whitespace, which the old comparison already forgave.
    [InlineData("tallinn", "Tallinn")]
    [InlineData("  TALLINN  ", "Tallinn")]
    public void MatchesNamesForTheSamePlace(string typed, string stored)
        => CityMatcher.IsSameCity(stored, typed).Should().BeTrue();

    [Theory]
    // A false positive routes somebody's move to the wrong end of the country,
    // which is worse than the honest miss this replaced. No fuzzy matching.
    [InlineData("Tallinn", "Tartu")]
    [InlineData("Rīga", "Liepāja")]
    [InlineData("Vilnius", "Kaunas")]
    [InlineData("Tallinn", "Tallinna mnt 5")]
    [InlineData("Narva", "Narva-Jõesuu")]
    public void DoesNotMatchDifferentPlaces(string a, string b)
        => CityMatcher.IsSameCity(a, b).Should().BeFalse();

    [Theory]
    [InlineData(null, "Tallinn")]
    [InlineData("", "Tallinn")]
    [InlineData("   ", "Tallinn")]
    [InlineData("Tallinn", null)]
    [InlineData("Tallinn", "")]
    public void BlankIsNeverAMatch(string? a, string? b)
        // An empty lead city must not sweep in every provider in the directory.
        => CityMatcher.IsSameCity(a, b).Should().BeFalse();

    [Fact]
    public void NormalizeIsStableEnoughToGroupBy()
    {
        // Callers may index on this, so equal cities must produce one key.
        CityMatcher.Normalize("Rīga").Should().Be(CityMatcher.Normalize("Riga"));
        CityMatcher.Normalize("Таллинн").Should().Be(CityMatcher.Normalize("Tallinn"));
        CityMatcher.Normalize("Harjumaa").Should().Be("tallinn");
        CityMatcher.Normalize("Lasnamäe").Should().Be("tallinn");
    }
}
