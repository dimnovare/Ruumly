using System.IO.Compression;
using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Services.Implementations;
using Xunit;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Covers the load-bearing pieces of the partner-docx → Dokobit signing engine:
/// run-merge-aware token discovery/fill (Open XML), the token vocabulary validation
/// rules, and the Dokobit status JSON parsing (both the sandbox-observed nested-signer
/// shape and the documented flat signer_info shape).
/// </summary>
public class ContractSigningEngineTests
{
    // ─── Open XML docx fill ────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal valid .docx where {{tenant_name}} is split across three runs
    /// (the Word gotcha) plus a paragraph with two more tokens.
    /// </summary>
    private static byte[] BuildDocx(string bodyXml)
    {
        const string contentTypes = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
            """;
        const string rootRels = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
            """;
        var document =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
            bodyXml +
            "</w:body></w:document>";

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                var e = zip.CreateEntry(path);
                using var s = e.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                s.Write(bytes, 0, bytes.Length);
            }
            Add("[Content_Types].xml", contentTypes);
            Add("_rels/.rels", rootRels);
            Add("word/document.xml", document);
        }
        return ms.ToArray();
    }

    private const string SplitRunBody =
        "<w:p><w:r><w:t>Tenant: {{tenant</w:t></w:r><w:r><w:t>_na</w:t></w:r><w:r><w:t>me}}</w:t></w:r></w:p>" +
        "<w:p><w:r><w:t xml:space=\"preserve\">Provider: {{supplier_name}}, ref {{unknown_token}}</w:t></w:r></w:p>";

    private static string ReadDocxText(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml")!;
        using var s = entry.Open();
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    [Fact]
    public void DiscoverTokens_reassembles_tokens_split_across_runs()
    {
        var svc = new OpenXmlContractDocumentService();

        var tokens = svc.DiscoverTokens(BuildDocx(SplitRunBody));

        // {{tenant_name}} must be found even though it spans 3 runs.
        tokens.Should().Contain("tenant_name");
        tokens.Should().Contain("supplier_name");
        tokens.Should().Contain("unknown_token");
    }

    [Fact]
    public void Fill_replaces_split_tokens_and_blanks_missing_to_empty()
    {
        var svc = new OpenXmlContractDocumentService();
        var values = new Dictionary<string, string>
        {
            ["tenant_name"]   = "Mari Maasikas",
            ["supplier_name"] = "Näidis Ladu OÜ",
            // unknown_token intentionally absent → must become empty string
        };

        var filled = svc.Fill(BuildDocx(SplitRunBody), values);
        var text   = ReadDocxText(filled);

        text.Should().Contain("Mari Maasikas");
        text.Should().Contain("Näidis Ladu OÜ");
        text.Should().NotContain("{{");           // no unresolved placeholders leak
        text.Should().NotContain("tenant_name");
    }

    [Fact]
    public void FillPlaceholders_unknown_token_becomes_empty()
    {
        var result = OpenXmlContractDocumentService.FillPlaceholders(
            "A {{known}} B {{missing}} C",
            new Dictionary<string, string> { ["known"] = "X" });

        result.Should().Be("A X B  C");
    }

    [Fact]
    public void AppendClause_always_adds_the_conditional_payment_text()
    {
        var svc  = new OpenXmlContractDocumentService();
        // Template that does NOT mention the clause at all.
        var docx = BuildDocx("<w:p><w:r><w:t>Storage lease body.</w:t></w:r></w:p>");

        var clause = ContractTokenVocabulary.PaymentConditionClause(2);
        var result = svc.AppendClause(docx, clause);
        var text   = ReadDocxText(result);

        text.Should().Contain("conditional upon payment");
        text.Should().Contain("within 2 hours");
        text.Should().Contain("automatically void");
        text.Should().Contain("Storage lease body."); // original content preserved
    }

    // ─── Token vocabulary validation ────────────────────────────────────────────

    [Fact]
    public void Vocabulary_flags_unknown_tokens_and_missing_core_tokens()
    {
        var detected = new[] { "tenant_name", "supplier_name", "unknown_token" };

        ContractTokenVocabulary.UnknownTokens(detected).Should().ContainSingle().Which.Should().Be("unknown_token");
        // listing_name + today are core but absent → warned (not blocked).
        ContractTokenVocabulary.MissingCoreTokens(detected).Should().Contain("listing_name");
        ContractTokenVocabulary.MissingCoreTokens(detected).Should().Contain("today");
    }

    [Fact]
    public void Vocabulary_recognizes_all_documented_tokens()
    {
        foreach (var t in ContractTokenVocabulary.Vocabulary)
            ContractTokenVocabulary.IsKnown(t.Token).Should().BeTrue($"{t.Token} is in the vocabulary");
    }

    // ─── Dokobit status parsing ─────────────────────────────────────────────────

    [Fact]
    public void ParseStatus_pending_nested_signers_shape_from_sandbox()
    {
        // Exact body returned by gateway-sandbox.dokobit.com for a fresh signing.
        const string json = """{"status":"pending","signers":{"1":{"status":"pending"}}}""";

        var result = DokobitService.ParseStatus(json);

        result.Status.Should().Be(Ruumly.Backend.Services.Interfaces.DokobitSigningStatus.Pending);
        result.VerifiedIdCode.Should().BeNull();
    }

    [Fact]
    public void ParseStatus_signed_nested_signer_captures_verified_identity()
    {
        const string json = """
            {"status":"signed","signers":{"1":{"status":"signed","code":"60001019906","country_code":"ee","signing_option":"smartid","name":"Mari","surname":"Maasikas","type":"qes"}}}
            """;

        var result = DokobitService.ParseStatus(json);

        result.Status.Should().Be(Ruumly.Backend.Services.Interfaces.DokobitSigningStatus.Completed);
        result.VerifiedIdCode.Should().Be("60001019906");
        result.SigningOption.Should().Be("smartid");
        result.VerifiedName.Should().Be("Mari Maasikas");
    }

    [Fact]
    public void ParseStatus_flat_signer_info_shape_is_also_handled()
    {
        // The shape some Dokobit docs/postbacks use: a top-level signer_info object.
        const string json = """
            {"status":"completed","signer_info":{"code":"38001085718","signing_option":"mobile","country_code":"ee"}}
            """;

        var result = DokobitService.ParseStatus(json);

        result.Status.Should().Be(Ruumly.Backend.Services.Interfaces.DokobitSigningStatus.Completed);
        result.VerifiedIdCode.Should().Be("38001085718");
        result.SigningOption.Should().Be("mobile");
    }

    [Fact]
    public void ParseStatus_completed_sandbox_shape_reads_certificate_identity()
    {
        // Sanitized shape returned by gateway-sandbox.dokobit.com after signing.
        const string json = """
            {
              "status": "completed",
              "signers": {
                "1": {
                  "status": "signed",
                  "signing_time": "2026-06-10 15:11:54",
                  "signature_id": "S-123"
                }
              },
              "structure": {
                "signatures": [
                  {
                    "certificate": {
                      "subject": {
                        "country": "EE",
                        "surname": "O’CONNEŽ-ŠUSLIK TESTNUMBER",
                        "name": "MARY ÄNN",
                        "serial_number": "PNOEE-60001019906"
                      }
                    },
                    "type": "qes"
                  }
                ]
              }
            }
            """;

        var result = DokobitService.ParseStatus(json);

        result.Status.Should().Be(Ruumly.Backend.Services.Interfaces.DokobitSigningStatus.Completed);
        result.VerifiedIdCode.Should().Be("60001019906");
        result.VerifiedName.Should().Be("MARY ÄNN O’CONNEŽ-ŠUSLIK TESTNUMBER");
    }

    [Fact]
    public async Task DownloadSignedDocument_follows_top_level_file_url_from_status()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/status.json"))
            {
                return JsonResponse("""
                    {
                      "status": "completed",
                      "file": "https://gateway-sandbox.dokobit.com/api/signing/session/download"
                    }
                    """);
            }

            if (req.RequestUri.AbsolutePath.EndsWith("/download"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent("%PDF-test"u8.ToArray()),
                };

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Signing:Dokobit:AccessToken"] = "test-token",
                ["Signing:Dokobit:Environment"] = "test",
            })
            .Build();
        var service = new DokobitService(
            new HttpClient(handler), config, NullLogger<DokobitService>.Instance);

        var bytes = await service.DownloadSignedDocumentAsync("session");

        bytes.Should().Equal("%PDF-test"u8.ToArray());
        handler.Requests.Should().Contain(u => u.AbsolutePath.EndsWith("/download"));
    }

    [Fact]
    public async Task DownloadSignedDocument_rejects_file_url_outside_dokobit_origin()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/status.json"))
            {
                return JsonResponse("""
                    {
                      "status": "completed",
                      "file": "https://attacker.example/signed.pdf"
                    }
                    """);
            }

            if (req.RequestUri.Host == "attacker.example")
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent("%PDF-stolen-token"u8.ToArray()),
                };

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Signing:Dokobit:AccessToken"] = "test-token",
                ["Signing:Dokobit:Environment"] = "test",
            })
            .Build();
        var service = new DokobitService(
            new HttpClient(handler), config, NullLogger<DokobitService>.Instance);

        var bytes = await service.DownloadSignedDocumentAsync("session");

        bytes.Should().BeNull();
        handler.Requests.Should().NotContain(u => u.Host == "attacker.example");
    }

    [Theory]
    [InlineData("pending", Ruumly.Backend.Services.Interfaces.DokobitSigningStatus.Pending)]
    [InlineData("signed", Ruumly.Backend.Services.Interfaces.DokobitSigningStatus.Completed)]
    [InlineData("completed", Ruumly.Backend.Services.Interfaces.DokobitSigningStatus.Completed)]
    [InlineData("declined", Ruumly.Backend.Services.Interfaces.DokobitSigningStatus.Cancelled)]
    [InlineData("expired", Ruumly.Backend.Services.Interfaces.DokobitSigningStatus.Error)]
    [InlineData("something_new", Ruumly.Backend.Services.Interfaces.DokobitSigningStatus.Pending)] // never flip to terminal
    public void MapStatus_maps_known_states_and_defaults_unknown_to_pending(
        string raw, Ruumly.Backend.Services.Interfaces.DokobitSigningStatus expected)
    {
        DokobitService.MapStatus(raw, null).Should().Be(expected);
    }

    [Fact]
    public void SplitName_splits_on_last_space()
    {
        DokobitService.SplitName("Mari Anne Maasikas").Should().Be(("Mari Anne", "Maasikas"));
        DokobitService.SplitName("Madonna").Should().Be(("Madonna", ""));
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }
}
