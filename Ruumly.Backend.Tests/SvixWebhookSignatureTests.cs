using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Ruumly.Backend.Helpers;

namespace Ruumly.Backend.Tests;

/// <summary>
/// The Resend bounce webhook is public and unauthenticated — this signature IS
/// the authentication. A forged bounce would retire a live provider's contact
/// address, so every rejection path is pinned here.
/// </summary>
public class SvixWebhookSignatureTests
{
    private const string Secret = "whsec_cnV1bWx5LXRlc3Qtd2ViaG9vay1zZWNyZXQ=";
    private const string Body   = """{"type":"email.bounced","data":{"to":["dead@example.ee"]}}""";
    private const string EventId = "msg_2abcDEF";

    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_770_000_000);

    private static string Sign(string id, long timestamp, string body, string secret = Secret)
    {
        var key = Convert.FromBase64String(secret["whsec_".Length..]);
        var signature = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{id}.{timestamp}.{body}"));
        return $"v1,{Convert.ToBase64String(signature)}";
    }

    private static SvixWebhookSignature.Result Verify(
        string? id = EventId,
        long? timestamp = null,
        string? signature = null,
        string body = Body,
        string? secret = Secret,
        DateTimeOffset? now = null)
    {
        var stamp = timestamp ?? Now.ToUnixTimeSeconds();
        return SvixWebhookSignature.Verify(
            id, stamp.ToString(), signature ?? Sign(id ?? "", stamp, body),
            body, secret, now ?? Now);
    }

    [Fact]
    public void ValidSignature_Passes() =>
        Verify().Should().Be(SvixWebhookSignature.Result.Valid);

    [Fact]
    public void SecretWithoutWhsecPrefix_Passes()
    {
        // Resend shows "whsec_..."; a founder pasting only the base64 half must work.
        var stamp = Now.ToUnixTimeSeconds();
        SvixWebhookSignature.Verify(
                EventId, stamp.ToString(), Sign(EventId, stamp, Body),
                Body, Secret["whsec_".Length..], Now)
            .Should().Be(SvixWebhookSignature.Result.Valid);
    }

    [Fact]
    public void TamperedBody_IsRejected()
    {
        var stamp = Now.ToUnixTimeSeconds();
        var forged = """{"type":"email.bounced","data":{"to":["competitor@example.ee"]}}""";

        SvixWebhookSignature.Verify(
                EventId, stamp.ToString(), Sign(EventId, stamp, Body), forged, Secret, Now)
            .Should().Be(SvixWebhookSignature.Result.NoMatch);
    }

    [Fact]
    public void WrongSecret_IsRejected()
    {
        var stamp = Now.ToUnixTimeSeconds();
        var attacker = "whsec_" + Convert.ToBase64String(Encoding.UTF8.GetBytes("not-our-secret"));

        SvixWebhookSignature.Verify(
                EventId, stamp.ToString(), Sign(EventId, stamp, Body, attacker), Body, Secret, Now)
            .Should().Be(SvixWebhookSignature.Result.NoMatch);
    }

    [Fact]
    public void SwappedEventId_IsRejected()
    {
        // The id is inside the signed content, so a replay under a fresh id
        // (which would defeat idempotency) cannot be signed with the old one.
        var stamp = Now.ToUnixTimeSeconds();

        SvixWebhookSignature.Verify(
                "msg_different", stamp.ToString(), Sign(EventId, stamp, Body), Body, Secret, Now)
            .Should().Be(SvixWebhookSignature.Result.NoMatch);
    }

    [Fact]
    public void OldCapture_IsExpired() =>
        Verify(timestamp: Now.AddMinutes(-10).ToUnixTimeSeconds())
            .Should().Be(SvixWebhookSignature.Result.Expired);

    [Fact]
    public void FarFutureTimestamp_IsExpired() =>
        Verify(timestamp: Now.AddMinutes(10).ToUnixTimeSeconds())
            .Should().Be(SvixWebhookSignature.Result.Expired);

    [Fact]
    public void WithinTolerance_Passes() =>
        Verify(timestamp: Now.AddMinutes(-4).ToUnixTimeSeconds())
            .Should().Be(SvixWebhookSignature.Result.Valid);

    [Theory]
    [InlineData(null, "1", "v1,x")]
    [InlineData("id", null, "v1,x")]
    [InlineData("id", "1", null)]
    public void MissingHeaders_AreRejected(string? id, string? timestamp, string? signature) =>
        SvixWebhookSignature.Verify(id, timestamp, signature, Body, Secret, Now)
            .Should().Be(SvixWebhookSignature.Result.MissingHeaders);

    [Fact]
    public void NonNumericTimestamp_IsRejected() =>
        SvixWebhookSignature.Verify(EventId, "not-a-number", "v1,x", Body, Secret, Now)
            .Should().Be(SvixWebhookSignature.Result.BadTimestamp);

    [Fact]
    public void MissingSecret_IsReportedSeparately_NotAsARejectedCaller() =>
        Verify(secret: "  ").Should().Be(SvixWebhookSignature.Result.BadSecret);

    [Fact]
    public void NonBase64Secret_IsReportedAsBadSecret() =>
        Verify(secret: "whsec_!!!not-base64!!!").Should().Be(SvixWebhookSignature.Result.BadSecret);

    [Fact]
    public void RotatingSecrets_SendSeveralSignatures_AnyMatchPasses()
    {
        var stamp = Now.ToUnixTimeSeconds();
        var stale = "whsec_" + Convert.ToBase64String(Encoding.UTF8.GetBytes("previous-secret"));
        var header = $"{Sign(EventId, stamp, Body, stale)} {Sign(EventId, stamp, Body)}";

        SvixWebhookSignature.Verify(EventId, stamp.ToString(), header, Body, Secret, Now)
            .Should().Be(SvixWebhookSignature.Result.Valid);
    }

    [Fact]
    public void UnknownSignatureVersion_IsIgnoredNotFatal()
    {
        var stamp = Now.ToUnixTimeSeconds();
        var header = $"v2,unknownscheme {Sign(EventId, stamp, Body)}";

        SvixWebhookSignature.Verify(EventId, stamp.ToString(), header, Body, Secret, Now)
            .Should().Be(SvixWebhookSignature.Result.Valid);
    }

    // ─── Payload parsing ──────────────────────────────────────────────────────

    [Fact]
    public void Parse_HardBounce_NormalizesTypeAndLowerCasesRecipient()
    {
        const string json = """
        {
          "type": "email.bounced",
          "created_at": "2026-08-10T09:15:00.000Z",
          "data": {
            "email_id": "abc",
            "to": ["Info@Kolimine.EE"],
            "subject": "Klient otsib kolimisteenust",
            "bounce": {
              "type": "Permanent",
              "subType": "NoEmail",
              "message": "The recipient's email address does not exist."
            }
          }
        }
        """;

        ResendWebhookEvent.TryParse(json, out var evt).Should().BeTrue();
        evt!.Type.Should().Be("email.bounced");
        evt.Recipients.Should().Equal("info@kolimine.ee");
        evt.BounceType.Should().Be("hard");
        evt.BounceSubType.Should().Be("NoEmail");
        evt.Reason.Should().Contain("does not exist");
        evt.IsDeliveryFailure.Should().BeTrue();
        evt.DisablesAddress.Should().BeTrue();
        evt.OccurredAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData("Transient")]
    [InlineData("Undetermined")]
    public void Parse_SoftAndAmbiguousBounces_DoNotDisableTheAddress(string bounceType)
    {
        var json = "{\"type\":\"email.bounced\",\"data\":{\"to\":[\"a@b.ee\"],"
                 + "\"bounce\":{\"type\":\"" + bounceType + "\",\"message\":\"Mailbox full\"}}}";

        ResendWebhookEvent.TryParse(json, out var evt).Should().BeTrue();
        evt!.BounceType.Should().Be("soft");
        evt.DisablesAddress.Should().BeFalse(
            "an ambiguous failure must never retire a live business's only address");
    }

    [Fact]
    public void Parse_Complaint_DisablesTheAddress()
    {
        const string json = """{"type":"email.complained","data":{"to":["a@b.ee"]}}""";

        ResendWebhookEvent.TryParse(json, out var evt).Should().BeTrue();
        evt!.BounceType.Should().Be("complaint");
        evt.DisablesAddress.Should().BeTrue();
    }

    [Fact]
    public void Parse_DeliveredEvent_IsNotADeliveryFailure()
    {
        const string json = """{"type":"email.delivered","data":{"to":["a@b.ee"]}}""";

        ResendWebhookEvent.TryParse(json, out var evt).Should().BeTrue();
        evt!.IsDeliveryFailure.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"data":{"to":["a@b.ee"]}}""")]
    public void Parse_Garbage_IsRejected(string json) =>
        ResendWebhookEvent.TryParse(json, out _).Should().BeFalse();
}
