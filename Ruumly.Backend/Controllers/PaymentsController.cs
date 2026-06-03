using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Filters;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;
using Sentry;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController(
    IPaymentService paymentService,
    ILogger<PaymentsController> logger,
    RuumlyDbContext db,
    IHttpContextAccessor http)
    : ControllerBase
{
    /// <summary>
    /// Initiates a Montonio payment for an invoice.
    /// Returns the URL to redirect the user to.
    /// Empty paymentUrl means "pay later" — no redirect.
    /// </summary>
    [RequireEmailVerified]
    [HttpPost("initiate")]
    [Authorize]
    [EnableRateLimiting("payment")]
    public async Task<IActionResult> Initiate(
        [FromBody] InitiatePaymentRequest request)
    {
        // Verify the caller owns this invoice.
        // Customers can only pay invoices linked to
        // their own bookings. Admins can initiate for any.
        var userId = http.HttpContext!.User.GetUserId();
        var role   = http.HttpContext!.User.GetUserRole();

        var invoice = await db.Invoices
            .Include(i => i.Booking).ThenInclude(b => b.Supplier)
            .FirstOrDefaultAsync(i => i.Id == request.InvoiceId);

        if (invoice is null)
            return NotFound(new { error = "Invoice not found" });

        if (role != UserRole.Admin &&
            invoice.Booking.UserId != userId)
            return Forbid();

        // Rebate suppliers are paid directly — Ruumly does not collect payment on their behalf.
        if (invoice.Booking.Supplier?.BillingModel == BillingModel.Rebate)
            return BadRequest(new { error = "Payment is not processed through Ruumly for this supplier." });

        var paymentUrl =
            await paymentService.CreatePaymentOrderAsync(
                request.InvoiceId,
                request.PaymentMethod,
                request.CustomerEmail,
                request.Locale ?? "et");

        return Ok(new { paymentUrl });
    }

    /// <summary>
    /// Montonio webhook — called by Montonio when
    /// payment is confirmed. Must always return 200.
    /// Verified by JWT signature, not auth header.
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(
        [FromBody] MontonioWebhookPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.Data))
        {
            SentrySdk.CaptureMessage(
                "Montonio webhook received with empty payload",
                scope =>
                {
                    scope.Level = SentryLevel.Warning;
                    scope.SetTag("webhook", "montonio");
                    scope.SetExtra("remote_ip",
                        http.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                });
            return BadRequest();
        }

        try
        {
            var ok = await paymentService.HandleWebhookAsync(payload.Data);

            if (!ok)
            {
                logger.LogWarning("Montonio webhook rejected or invalid");
                SentrySdk.CaptureMessage(
                    "Montonio webhook rejected: JWT verification or status check failed",
                    scope =>
                    {
                        scope.Level = SentryLevel.Warning;
                        scope.SetTag("webhook", "montonio");
                        // Avoid logging the raw JWT — it may contain sensitive payment data.
                        scope.SetExtra("payload_length", payload.Data.Length);
                    });
            }

            return Ok();
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex, scope =>
            {
                scope.SetTag("webhook", "montonio");
                scope.SetExtra("payload_length", payload.Data.Length);
                scope.SetExtra("remote_ip",
                    http.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            });
            // Webhook handlers must return 200 so Montonio does not retry indefinitely.
            // The exception is captured; swallow it here after alerting Sentry.
            logger.LogError(ex, "Unhandled exception in Montonio webhook handler");
            return Ok();
        }
    }

    public record InitiatePaymentRequest(
        Guid    InvoiceId,
        string  PaymentMethod,
        string  CustomerEmail,
        string? Locale);

    public record MontonioWebhookPayload(
        string Data);
}
