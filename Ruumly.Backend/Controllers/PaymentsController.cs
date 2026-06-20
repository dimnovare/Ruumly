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

        var supplier = invoice.Booking.Supplier;
        var method = request.PaymentMethod.Trim().ToLowerInvariant();

        // ── Bank transfer (no PSP) ────────────────────────────────────────────────
        // The customer pays to the platform bank account and we reconcile manually
        // (admin marks the invoice Paid on receipt). Requires the partner to have
        // opted into direct payment, the platform to have bank transfer enabled, and
        // the rental to be signed first. Returns payment instructions, not a redirect.
        if (method == "bank_transfer")
        {
            if (supplier?.DirectPaymentEnabled != true)
                return BadRequest(new { error = ErrorMessages.Get("DIRECT_PAYMENT_DISABLED", Request.GetLang()) });

            var enabled = await GetSettingAsync("bankTransfer.enabled");
            if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { error = ErrorMessages.Get("DIRECT_PAYMENT_DISABLED", Request.GetLang()) });

            // Sign-then-pay: the rental must be signed before we issue payment instructions.
            var hasSigned = await db.SignedContracts
                .AnyAsync(c => c.BookingId == invoice.BookingId && c.Status == "completed");
            if (!hasSigned)
                return Conflict(new { code = ContractNotSignedException.Code,
                    error = "This rental must be signed before payment can be made." });

            invoice.PaymentMethod = "bank_transfer";
            if (invoice.Status == InvoiceStatus.Pending)
                invoice.Status = InvoiceStatus.AwaitingPayment;
            await db.SaveChangesAsync();

            var iban = await GetSettingAsync("bankTransfer.iban");
            return Ok(new
            {
                paymentUrl = (string?)null,
                bankTransfer = new
                {
                    available   = !string.IsNullOrWhiteSpace(iban),
                    amount      = invoice.Amount,
                    currency    = "EUR",
                    reference   = "RUUMLY-" + invoice.BookingId.ToString("N")[..8].ToUpperInvariant(),
                    accountName = await GetSettingAsync("bankTransfer.accountName") ?? "",
                    iban        = iban ?? "",
                    bic         = await GetSettingAsync("bankTransfer.bic") ?? "",
                    bankName    = await GetSettingAsync("bankTransfer.bankName") ?? "",
                },
            });
        }

        var isDirectPayment = method == "later";

        if (isDirectPayment)
        {
            if (supplier?.DirectPaymentEnabled != true)
                return BadRequest(new
                {
                    error = ErrorMessages.Get("DIRECT_PAYMENT_DISABLED", Request.GetLang())
                });
        }
        else
        {
            if (supplier?.RuumlyPaymentEnabled != true)
                return BadRequest(new
                {
                    error = ErrorMessages.Get("RUUMLY_PAYMENT_DISABLED", Request.GetLang())
                });
        }

        // Rebate suppliers are paid directly — Ruumly does not collect payment on their behalf.
        if (!isDirectPayment && supplier?.BillingModel == BillingModel.Rebate)
            return BadRequest(new { error = "Payment is not processed through Ruumly for this supplier." });

        string paymentUrl;
        try
        {
            paymentUrl = await paymentService.CreatePaymentOrderAsync(
                request.InvoiceId,
                request.PaymentMethod,
                request.CustomerEmail,
                request.Locale ?? "et");
        }
        catch (ContractNotSignedException ex)
        {
            // Sign-then-pay: the rental must be signed before payment can be initiated.
            logger.LogInformation(
                "Payment initiation rejected for invoice {InvoiceId}: contract not signed",
                request.InvoiceId);
            return Conflict(new { code = ContractNotSignedException.Code, error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // Montonio rejected the request or returned an error (already captured in service).
            // Surface a user-friendly message; avoid leaking internal details.
            SentrySdk.CaptureException(ex, scope =>
            {
                scope.SetExtra("invoiceId", request.InvoiceId.ToString());
                scope.SetExtra("paymentMethod", request.PaymentMethod);
                scope.SetTag("payment", "initiate_failed");
            });
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = ex.Message });
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex, scope =>
            {
                scope.SetExtra("invoiceId", request.InvoiceId.ToString());
                scope.SetExtra("paymentMethod", request.PaymentMethod);
                scope.SetTag("payment", "initiate_error");
            });
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Payment initiation failed. Please try again." });
        }

        return Ok(new { paymentUrl });
    }

    /// <summary>Reads a single PlatformSettings value by key (null if unset).</summary>
    private Task<string?> GetSettingAsync(string key) =>
        db.PlatformSettings.Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync();

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
                logger.LogWarning("Montonio webhook rejected: missing reference or unhandled status");
                SentrySdk.CaptureMessage(
                    "Montonio webhook rejected: missing merchant_reference or unhandled status",
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
        catch (WebhookSignatureException ex)
        {
            // JWT signature is invalid — this is not a transient Montonio retry scenario.
            // Return 400 so the caller knows the token was rejected; already captured in service.
            logger.LogWarning(ex, "Montonio webhook rejected: invalid JWT signature");
            return BadRequest(new { error = "Webhook signature verification failed." });
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
            // For genuine processing errors, return 200 so Montonio retries the webhook.
            // The exception is already captured in Sentry above.
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
