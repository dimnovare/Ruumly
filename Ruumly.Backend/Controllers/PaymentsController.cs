using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController(
    IPaymentService paymentService,
    ILogger<PaymentsController> logger)
    : ControllerBase
{
    /// <summary>
    /// Initiates a Montonio payment for an invoice.
    /// Returns the URL to redirect the user to.
    /// Empty paymentUrl means "pay later" — no redirect.
    /// </summary>
    [HttpPost("initiate")]
    [Authorize]
    public async Task<IActionResult> Initiate(
        [FromBody] InitiatePaymentRequest request)
    {
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
            return BadRequest();

        var ok = await paymentService
            .HandleWebhookAsync(payload.Data);

        if (!ok)
            logger.LogWarning(
                "Montonio webhook rejected or invalid");

        return Ok();
    }

    public record InitiatePaymentRequest(
        Guid    InvoiceId,
        string  PaymentMethod,
        string  CustomerEmail,
        string? Locale);

    public record MontonioWebhookPayload(
        string Data);
}
