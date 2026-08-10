using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Services.Interfaces;
using Sentry;

namespace Ruumly.Backend.Controllers;

/// <summary>
/// Resend delivery webhook — the only way a bounce ever gets back into Ruumly.
///
/// Public and unauthenticated by necessity (Resend cannot hold our JWT), so the
/// SVIX SIGNATURE IS THE AUTHENTICATION. Without it anyone could POST a forged
/// bounce and silently retire a competitor's contact address, which is exactly
/// the failure this feature exists to prevent. Never add a bypass.
///
/// Configure in the Resend dashboard (Webhooks → Add endpoint):
///   URL     https://api.ruumly.eu/api/webhooks/resend
///   Events  email.bounced, email.complained
///   Secret  copy the "whsec_..." signing secret into RESEND__WEBHOOKSECRET
/// </summary>
[ApiController]
[Route("api/webhooks")]
[AllowAnonymous]
public sealed class ResendWebhookController(
    IEmailDeliveryTracker tracker,
    IConfiguration config,
    ILogger<ResendWebhookController> logger) : ControllerBase
{
    /// <summary>Bodies are a few hundred bytes; anything larger is not Resend.</summary>
    private const int MaxBodyBytes = 256 * 1024;

    [HttpPost("resend")]
    [EnableRateLimiting("webhook")]
    public async Task<IActionResult> Resend(CancellationToken ct)
    {
        var secret = config["Resend:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            // Fail closed and loudly: silently 200-ing would look healthy in the
            // Resend dashboard while every bounce is thrown away — the exact
            // invisible-loss bug this endpoint exists to end.
            logger.LogError(
                "Resend webhook received but Resend:WebhookSecret is not configured — event dropped.");
            SentrySdk.CaptureMessage("Resend webhook secret missing", SentryLevel.Error);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Webhook is not configured." });
        }

        var body = await ReadBodyAsync(ct);
        if (body is null)
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new { error = "Payload too large." });

        // Svix names its headers svix-*; the Standard Webhooks spec uses webhook-*.
        // Resend sends svix-*, but accepting both costs nothing and survives a
        // rename on their side.
        var id        = Header("svix-id",        "webhook-id");
        var timestamp = Header("svix-timestamp", "webhook-timestamp");
        var signature = Header("svix-signature", "webhook-signature");

        var verification = SvixWebhookSignature.Verify(
            id, timestamp, signature, body, secret, DateTimeOffset.UtcNow);
        if (verification != SvixWebhookSignature.Result.Valid)
        {
            logger.LogWarning(
                "Resend webhook rejected: {Reason} (id {EventId}).", verification, id ?? "-");
            SentrySdk.CaptureMessage("Resend webhook signature rejected", scope =>
            {
                scope.Level = SentryLevel.Warning;
                scope.SetTag("webhook", "resend");
                scope.SetExtra("reason", verification.ToString());
                scope.SetExtra("remote_ip",
                    HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            });

            // A missing secret on OUR side is 503 above; everything here is the
            // caller failing to prove who they are.
            return verification == SvixWebhookSignature.Result.BadSecret
                ? StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { error = "Webhook is not configured." })
                : Unauthorized(new { error = "Webhook signature verification failed." });
        }

        if (!ResendWebhookEvent.TryParse(body, out var evt) || evt is null)
        {
            logger.LogWarning("Resend webhook {EventId} had an unreadable body.", id);
            return BadRequest(new { error = "Malformed webhook payload." });
        }

        // Delivered/opened/clicked are accepted and ignored, so turning extra
        // events on in the dashboard can never start failing deliveries.
        if (!evt.IsDeliveryFailure)
            return Ok(new { received = true, applied = false, reason = "ignored_event" });

        if (evt.Recipients.Count == 0)
            return Ok(new { received = true, applied = false, reason = "no_recipient" });

        try
        {
            var outcome = await tracker.RecordAsync(id!, evt, ct);
            if (outcome.Duplicate)
                return Ok(new { received = true, applied = false, reason = "duplicate" });

            logger.LogInformation(
                "Resend {EventType} for {Recipient} ({BounceType}): {Flagged} supplier(s) flagged unusable, {Rows} outreach row(s) updated.",
                evt.Type, evt.Recipients[0], evt.BounceType ?? "unknown",
                outcome.SuppliersFlagged, outcome.OutreachRowsUpdated);

            return Ok(new
            {
                received            = true,
                applied             = true,
                suppliersFlagged    = outcome.SuppliersFlagged,
                suppliersTouched    = outcome.SuppliersTouched,
                outreachRowsUpdated = outcome.OutreachRowsUpdated,
            });
        }
        catch (Exception ex)
        {
            // Signature already proved the caller — a failure here is ours, so
            // return 500 and let Resend retry into the idempotency guard.
            logger.LogError(ex, "Resend webhook {EventId} failed to apply.", id);
            SentrySdk.CaptureException(ex, scope =>
            {
                scope.SetTag("webhook", "resend");
                scope.SetExtra("event_id", id ?? "unknown");
                scope.SetExtra("event_type", evt.Type);
            });
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Webhook processing failed." });
        }
    }

    private string? Header(params string[] names)
    {
        foreach (var name in names)
        {
            if (Request.Headers.TryGetValue(name, out var values))
            {
                var value = values.ToString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }

        return null;
    }

    /// <summary>
    /// The signature covers the RAW bytes, so the body must be read verbatim —
    /// never round-tripped through a model binder. Returns null when oversized.
    /// </summary>
    private async Task<string?> ReadBodyAsync(CancellationToken ct)
    {
        if (Request.ContentLength > MaxBodyBytes) return null;

        using var buffer = new MemoryStream();
        var chunk = new byte[8 * 1024];
        int read;
        while ((read = await Request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes) return null;
            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
