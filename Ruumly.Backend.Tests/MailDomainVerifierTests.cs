using System.Text.Json;
using FluentAssertions;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Services.Implementations;

namespace Ruumly.Backend.Tests;

/// <summary>
/// The DNS half of the deliverability guard. Everything here is the pure decision
/// logic — how a resolver answer becomes a verdict, and what is safe to put in a
/// query string. No test in this file touches the network.
///
/// The governing rule is asymmetric: only a POSITIVE proof of undeliverability may
/// hold a provider back. Anything ambiguous has to come back Inconclusive, because
/// the caller treats Inconclusive as "send it".
/// </summary>
public class MailDomainVerifierTests
{
    private static DnsMailDomainVerifier.Verdict Verdict(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return DnsMailDomainVerifier.Interpret(doc.RootElement);
    }

    // ─── Reading a resolver answer ────────────────────────────────────────────

    [Fact]
    public void ADomainWithMxRecords_IsDeliverable()
    {
        Verdict("""
        {"Status":0,"Answer":[
          {"name":"gmail.com.","type":15,"TTL":3600,"data":"5 gmail-smtp-in.l.google.com."},
          {"name":"gmail.com.","type":15,"TTL":3600,"data":"10 alt1.gmail-smtp-in.l.google.com."}
        ]}
        """).Should().Be(DnsMailDomainVerifier.Verdict.Deliverable);
    }

    [Fact]
    public void ADomainThatDoesNotExist_IsUndeliverable()
    {
        // NXDOMAIN — kapsel24.ee and esvo.ee in the 2026-08-09 audit.
        Verdict("""{"Status":3}""")
            .Should().Be(DnsMailDomainVerifier.Verdict.Undeliverable);
    }

    [Fact]
    public void ADomainThatResolvesButPublishesNoMx_IsUndeliverable()
    {
        // t49.ee / noortegija.ee / zebra.lt: A records only, TCP :25 refused.
        // RFC 5321 allows an implicit MX at the A record, so this is a judgement
        // call — and the audit settled it by probing port 25 on all three.
        Verdict("""{"Status":0}""")
            .Should().Be(DnsMailDomainVerifier.Verdict.Undeliverable);

        Verdict("""{"Status":0,"Answer":[]}""")
            .Should().Be(DnsMailDomainVerifier.Verdict.Undeliverable);
    }

    [Fact]
    public void AnAnswerCarryingOnlyNonMxRecords_IsUndeliverable()
    {
        // A CNAME chain that never reaches an MX is still no mail exchanger.
        Verdict("""
        {"Status":0,"Answer":[
          {"name":"t49.ee.","type":5,"TTL":300,"data":"proxy.cloudflare.net."}
        ]}
        """).Should().Be(DnsMailDomainVerifier.Verdict.Undeliverable);
    }

    [Fact]
    public void ANullMxRecord_IsUndeliverable()
    {
        // RFC 7505: "0 ." is a domain explicitly declaring it accepts no mail.
        // Read naively it looks like a perfectly good MX answer.
        Verdict("""
        {"Status":0,"Answer":[{"name":"x.ee.","type":15,"TTL":3600,"data":"0 ."}]}
        """).Should().Be(DnsMailDomainVerifier.Verdict.Undeliverable);
    }

    [Fact]
    public void ANullMxAlongsideARealMx_IsStillDeliverable()
    {
        Verdict("""
        {"Status":0,"Answer":[
          {"name":"x.ee.","type":15,"TTL":3600,"data":"0 ."},
          {"name":"x.ee.","type":15,"TTL":3600,"data":"10 mail.x.ee."}
        ]}
        """).Should().Be(DnsMailDomainVerifier.Verdict.Deliverable);
    }

    [Theory]
    [InlineData("""{"Status":2}""")]                  // SERVFAIL
    [InlineData("""{"Status":5}""")]                  // REFUSED
    [InlineData("""{"Answer":[]}""")]                 // no Status at all
    [InlineData("""{"Status":"nope"}""")]             // Status not a number
    public void AnAnswerThatSettlesNothing_IsInconclusive(string json)
    {
        // Inconclusive means the provider still gets mailed. A resolver having a
        // bad day must never look like evidence against a business.
        Verdict(json).Should().Be(DnsMailDomainVerifier.Verdict.Inconclusive);
    }

    // ─── What is safe to ask about ────────────────────────────────────────────

    [Theory]
    [InlineData("gmail.com",        "gmail.com")]
    [InlineData("KAPSEL24.EE",      "kapsel24.ee")]
    [InlineData("mail.viada.lt.",   "mail.viada.lt")]   // trailing root dot
    [InlineData("xn--brger-kva.de", "xn--brger-kva.de")]
    public void AQueryableHostname_IsNormalised(string input, string expected) =>
        DnsMailDomainVerifier.ToAsciiHostname(input).Should().Be(expected);

    [Fact]
    public void AnInternationalDomain_IsConvertedToPunycode() =>
        DnsMailDomainVerifier.ToAsciiHostname("bürger.de").Should().Be("xn--brger-kva.de");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("localhost")]                       // single label
    [InlineData("example..com")]                    // empty label
    [InlineData("-example.com")]                    // leading hyphen
    [InlineData("example-.com")]                    // trailing hyphen
    [InlineData("exa mple.com")]                    // whitespace
    [InlineData("example.com/../evil")]             // path traversal
    [InlineData("example.com&type=A")]              // query-string injection
    [InlineData("exa\"mple.com")]
    public void AnythingNotAPlainHostname_IsRefused(string input)
    {
        // Refused means null, which the caller reads as Inconclusive — the
        // provider is mailed rather than blamed for a name we cannot check.
        DnsMailDomainVerifier.ToAsciiHostname(input).Should().BeNull();
    }

    [Fact]
    public void OverlongNamesAndLabels_AreRefused()
    {
        DnsMailDomainVerifier.ToAsciiHostname(new string('a', 64) + ".com").Should().BeNull();
        DnsMailDomainVerifier.ToAsciiHostname(
            string.Join('.', Enumerable.Repeat(new string('a', 60), 5))).Should().BeNull();
    }

    // ─── The domain of an address ─────────────────────────────────────────────

    [Theory]
    [InlineData("info@kapsel24.ee",  "kapsel24.ee")]
    [InlineData("  Info@KAPSEL24.EE ", "kapsel24.ee")]
    [InlineData("rekota@zebra.lt",   "zebra.lt")]
    public void DomainOf_LowerCasesAndTrims(string email, string expected) =>
        EmailValidation.DomainOf(email).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("info@@ecsecobaltic.lt")]   // real row from the wave-2 research
    [InlineData("@example.com")]
    [InlineData("user@")]
    public void DomainOf_IsNullForAnythingNotAValidAddress(string? email) =>
        EmailValidation.DomainOf(email).Should().BeNull();
}
