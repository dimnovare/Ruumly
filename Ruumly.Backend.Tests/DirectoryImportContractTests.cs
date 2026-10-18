using System.Text.Json;
using FluentAssertions;
using Ruumly.Backend.DTOs.Requests;

namespace Ruumly.Backend.Tests;

/// <summary>
/// The directory bulk import used to accept a payload it did not understand.
///
/// System.Text.Json skips unrecognised properties by default, so a caller could
/// post rows carrying priceFrom / priceUnit / priceNote - fields the Supplier
/// table genuinely has - watch every row be created WITHOUT them, and receive
/// 200 "created". Nothing in the response said a column had been dropped.
///
/// These tests pin the refusal, and pin that it is scoped to this DTO rather
/// than turned on globally: the Montonio, Dokobit and Resend callbacks share the
/// same serializer options and those providers add payload fields without notice.
/// </summary>
public class DirectoryImportContractTests
{
    // Mirrors Program.cs AddJsonOptions so the test exercises the real contract.
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private const string ValidRow = """
        {"name":"Ladu OÜ","slug":"ladu-tallinn","city":"Tallinn",
         "lat":59.437,"lng":24.7536,"serviceTypes":["warehouse"]}
        """;

    [Fact]
    public void AKnownRow_StillDeserializes()
    {
        var row = JsonSerializer.Deserialize<DirectorySupplierRow>(ValidRow, Options);

        row.Should().NotBeNull();
        row!.Slug.Should().Be("ladu-tallinn");
        row.ServiceTypes.Should().BeEquivalentTo(["warehouse"]);
    }

    [Fact]
    public void APriceFieldTheImportCannotStore_IsRefused_NotSilentlyDropped()
    {
        // The exact payload shape a price backfill would post. Before the DTO
        // refused unmapped members this deserialized happily, the price vanished,
        // and the endpoint reported success.
        const string withPrice = """
            {"name":"Ladu OÜ","slug":"ladu-tallinn","city":"Tallinn",
             "lat":59.437,"lng":24.7536,"serviceTypes":["warehouse"],
             "priceFrom":49,"priceUnit":"/kuu"}
            """;

        var act = () => JsonSerializer.Deserialize<DirectorySupplierRow>(withPrice, Options);

        act.Should().Throw<JsonException>(
            "a field the import cannot store must fail loudly, not be discarded");
    }

    [Fact]
    public void TheRefusalNamesTheOffendingField()
    {
        // An admin pasting a 400-row payload needs to know WHICH key was wrong.
        const string typo = """
            {"name":"Ladu OÜ","slug":"ladu-tallinn","city":"Tallinn",
             "lat":59.437,"lng":24.7536,"serviceTypes":["warehouse"],
             "websiteURL":"https://ladu.ee"}
            """;

        var act = () => JsonSerializer.Deserialize<DirectorySupplierRow>(typo, Options);

        act.Should().Throw<JsonException>().Which.Message.Should().Contain("websiteURL");
    }

    [Fact]
    public void TheRefusalIsScopedToThisDto_NotGlobal()
    {
        // The guard against someone "simplifying" this into AddJsonOptions. If a
        // global Disallow were set, this anonymous-shaped payload would also throw
        // - and so would the Montonio / Dokobit / Resend callbacks, which share
        // those options and receive provider-defined fields we do not model.
        var act = () => JsonSerializer.Deserialize<Dictionary<string, object>>(
            """{"anythingAtAll":1,"weDoNotModelThis":true}""", Options);

        act.Should().NotThrow();
    }
}
