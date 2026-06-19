using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;
using Sentry;

namespace Ruumly.Backend.Services.Implementations;

public class MontonioPaymentService(
    RuumlyDbContext db,
    IConfiguration config,
    IHttpClientFactory httpFactory,
    ILogger<MontonioPaymentService> logger,
    IHttpContextAccessor httpAccessor)
    : IPaymentService
{
    private string AccessKey =>
        config["Montonio:AccessKey"] ?? "";
    private string SecretKey =>
        config["Montonio:SecretKey"] ?? "";

    /// <summary>
    /// Toggle Montonio sandbox via env var MONTONIO__USESANDBOX=true (or appsettings).
    /// Sandbox base URL: https://sandbox-api.montonio.com
    /// Production base URL: https://api.montonio.com
    /// Keys are different per environment — set MONTONIO__ACCESSKEY and MONTONIO__SECRETKEY accordingly.
    /// </summary>
    private string ApiUrl
    {
        get
        {
            var useSandbox = config.GetValue<bool>("Montonio:UseSandbox");
            if (useSandbox)
                return config["Montonio:SandboxApiUrl"] ?? "https://sandbox-api.montonio.com";
            return config["Montonio:ApiUrl"] ?? "https://api.montonio.com";
        }
    }

    private string ReturnUrl =>
        config["Montonio:ReturnUrl"] ?? "";
    private string NotifyUrl =>
        config["Montonio:NotifyUrl"] ?? "";

    // Timeout for outbound Montonio API calls — prevents hung payment threads.
    private static readonly TimeSpan MontonioHttpTimeout = TimeSpan.FromSeconds(15);

    public async Task<string> CreatePaymentOrderAsync(
        Guid invoiceId,
        string paymentMethod,
        string customerEmail,
        string customerLocale)
    {
        var invoice = await db.Invoices
            .Include(i => i.Booking)
                .ThenInclude(b => b.Listing)
            .FirstOrDefaultAsync(i => i.Id == invoiceId)
            ?? throw new KeyNotFoundException(
                "Invoice not found");

        if (paymentMethod == "later")
        {
            invoice.PaymentMethod = "later";
            await db.SaveChangesAsync();
            return "";
        }

        // Sign-then-pay guard (acceptance criterion #1): a Montonio payment order cannot be
        // created for a booking that has no COMPLETED signed contract. Applies only to the
        // pay-now path (the "later"/rebate path returned above and never reaches Montonio).
        var hasCompletedContract = await db.SignedContracts
            .AnyAsync(c => c.BookingId == invoice.BookingId && c.Status == "completed");
        if (!hasCompletedContract)
        {
            logger.LogWarning(
                "Payment initiation blocked for invoice {InvoiceId} (booking {BookingId}): no completed signed contract",
                invoiceId, invoice.BookingId);
            throw new ContractNotSignedException(
                "This rental must be signed before payment can be made.");
        }

        // Guard: if payment already initiated and URL was saved, don't create a duplicate order.
        // This makes the endpoint idempotent on browser back+resubmit.
        if (invoice.Status == InvoiceStatus.AwaitingPayment
            && !string.IsNullOrEmpty(invoice.PaymentOrderId))
        {
            logger.LogInformation(
                "Duplicate initiate request for invoice {InvoiceId} — PaymentOrderId {OrderId} already set, skipping",
                invoiceId, invoice.PaymentOrderId);
            // We no longer have the URL (not persisted), so we must create a new order.
            // Fall through — Montonio allows creating a new order; the old one will expire.
        }

        var orderId = Guid.NewGuid().ToString();
        var locale  = customerLocale switch
        {
            "en" => "en",
            "ru" => "ru",
            _    => "et",
        };

        var payload = new
        {
            access_key         = AccessKey,
            merchant_reference = orderId,
            return_url  = $"{ReturnUrl}?invoice={invoiceId}",
            notification_url   = NotifyUrl,
            currency    = "EUR",
            grand_total = invoice.Amount,
            locale,
            billing_address = new { email = customerEmail },
            payment = new
            {
                amount   = invoice.Amount,
                currency = "EUR",
                method_options = new
                {
                    payment_methods = new[]
                    {
                        paymentMethod == "card"
                            ? "card" : "banklink"
                    }
                }
            }
        };

        var key   = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(SecretKey));
        var creds = new SigningCredentials(
            key, SecurityAlgorithms.HmacSha256);
        var jwt   = new JwtSecurityToken(
            claims: [new System.Security.Claims.Claim(
                "data",
                JsonSerializer.Serialize(payload))],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: creds);
        var jwtString = new JwtSecurityTokenHandler()
            .WriteToken(jwt);

        // Save the orderId to DB BEFORE calling Montonio so it survives a crash
        // between the HTTP call succeeding and the save completing.
        invoice.PaymentOrderId = orderId;
        invoice.PaymentMethod  = paymentMethod;
        invoice.Status         = InvoiceStatus.AwaitingPayment;
        await db.SaveChangesAsync();

        // Abandoned-checkout cleanup is handled by StaleBookingCleanupJob: a Pending booking
        // whose invoice never reaches Paid within the configured window
        // (PlatformSettings "booking.signedUnpaidExpiryHours", default 24h) is auto-cancelled
        // and any attached signed contract is voided. No refund path — no money was taken.

        string paymentUrl;
        try
        {
            using var cts = new CancellationTokenSource(MontonioHttpTimeout);
            var http = httpFactory.CreateClient();
            var res  = await http.PostAsJsonAsync(
                $"{ApiUrl}/merchant/payment-orders",
                new { data = jwtString },
                cts.Token);

            if (!res.IsSuccessStatusCode)
            {
                var errorBody = await res.Content.ReadAsStringAsync();
                logger.LogError(
                    "Montonio order creation failed: HTTP {StatusCode} for invoice {InvoiceId}, orderId {OrderId}. Body: {Body}",
                    (int)res.StatusCode, invoiceId, orderId, errorBody);
                SentrySdk.CaptureMessage(
                    $"Montonio order creation failed: HTTP {(int)res.StatusCode}",
                    scope =>
                    {
                        scope.SetExtra("invoiceId",    invoiceId.ToString());
                        scope.SetExtra("orderId",      orderId);
                        scope.SetExtra("statusCode",   (int)res.StatusCode);
                        scope.SetExtra("responseBody", errorBody);
                    },
                    SentryLevel.Error);
                throw new InvalidOperationException(
                    ErrorMessages.Get("PAYMENT_PROVIDER_UNAVAILABLE",
                        httpAccessor.HttpContext?.Request.GetLang() ?? "et"));
            }

            var body = await res.Content
                .ReadFromJsonAsync<MontonioOrderResponse>()
                ?? throw new InvalidOperationException(
                    "Invalid Montonio response: null body");

            if (string.IsNullOrWhiteSpace(body.PaymentUrl))
                throw new InvalidOperationException(
                    "Invalid Montonio response: missing payment_url");

            paymentUrl = body.PaymentUrl;
        }
        catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
        {
            // Timeout — orderId is already saved so webhook can still land correctly.
            logger.LogError(ex,
                "Montonio API call timed out after {Timeout}s for invoice {InvoiceId}",
                MontonioHttpTimeout.TotalSeconds, invoiceId);
            SentrySdk.CaptureException(ex, scope =>
            {
                scope.SetExtra("invoiceId", invoiceId.ToString());
                scope.SetExtra("orderId",   orderId);
            });
            throw new InvalidOperationException(
                ErrorMessages.Get("PAYMENT_PROVIDER_UNAVAILABLE",
                    httpAccessor.HttpContext?.Request.GetLang() ?? "et"));
        }

        return paymentUrl;
    }

    public async Task<bool> HandleWebhookAsync(string token)
    {
        string? merchantReference = null;
        try
        {
            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(SecretKey));
            var handler = new JwtSecurityTokenHandler();
            handler.ValidateToken(token,
                new TokenValidationParameters
                {
                    ValidateIssuer   = false,
                    ValidateAudience = false,
                    IssuerSigningKey = key,
                    ClockSkew        = TimeSpan.FromSeconds(30),
                }, out var validated);

            var jwt = (JwtSecurityToken)validated;

            // Montonio wraps the payload in a "data" claim (same structure as the
            // outgoing order JWT). Extract merchant_reference and payment_status
            // from the nested JSON object.
            string?  ref_      = null;
            string?  status    = null;
            // Amount + currency reported by the (already signature-verified) webhook payload.
            // Captured here so the 'paid' branch can assert they match the invoice before
            // transitioning to Paid (defence against an amount/currency-mismatched event).
            decimal? webhookAmount   = null;
            string?  webhookCurrency = null;

            // Try nested "data" claim first (standard Montonio webhook format)
            var dataClaim = jwt.Claims.FirstOrDefault(c => c.Type == "data")?.Value;
            if (!string.IsNullOrEmpty(dataClaim))
            {
                try
                {
                    using var doc = JsonDocument.Parse(dataClaim);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("merchant_reference", out var refEl))
                        ref_ = refEl.GetString();
                    if (root.TryGetProperty("payment_status", out var statusEl))
                        status = statusEl.GetString();
                    // Montonio echoes the order total as "grand_total" (falling back to "amount").
                    if (root.TryGetProperty("grand_total", out var grandEl)
                        && grandEl.TryGetDecimal(out var grand))
                        webhookAmount = grand;
                    else if (root.TryGetProperty("amount", out var amtEl)
                        && amtEl.TryGetDecimal(out var amt))
                        webhookAmount = amt;
                    if (root.TryGetProperty("currency", out var curEl))
                        webhookCurrency = curEl.GetString();
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "Montonio webhook: failed to parse 'data' claim JSON");
                }
            }

            // Fall back to flat top-level claims (some Montonio sandbox versions)
            if (ref_ is null)
                ref_ = jwt.Claims.FirstOrDefault(c => c.Type == "merchant_reference")?.Value;
            if (status is null)
                status = jwt.Claims.FirstOrDefault(c => c.Type == "payment_status")?.Value;

            merchantReference = ref_;

            if (string.IsNullOrEmpty(ref_))
            {
                logger.LogWarning(
                    "Montonio webhook: missing merchant_reference. status={Status}",
                    status);
                return false;
            }

            // Handle cancellation / refund — update invoice and booking accordingly.
            if (status == "cancelled" || status == "refunded")
            {
                logger.LogInformation(
                    "Montonio webhook: payment {Status} for merchant_reference {Ref}",
                    status, ref_);

                var cancelledInvoice = await db.Invoices
                    .Include(i => i.Booking)
                    .FirstOrDefaultAsync(i => i.PaymentOrderId == ref_);

                if (cancelledInvoice is not null)
                {
                    // Terminal-state / idempotency guard. Without this a stale, duplicate or
                    // out-of-order webhook can corrupt a genuinely Paid invoice:
                    //   - A late 'cancelled' (abandoned-checkout) event arriving AFTER a successful
                    //     payment must be ignored — it does not mean the paid order was voided.
                    //   - Replays of an already-applied 'refunded'/cancellation are no-ops.
                    // So we only ACT on the legitimate transition for each event type:
                    //   'cancelled' acts only on an AwaitingPayment invoice (checkout abandoned);
                    //   'refunded'  acts only on a Paid invoice (money was actually returned).
                    // Note: InvoiceStatus has no 'Cancelled' member, so a voided checkout is
                    // recorded as Refunded (consistent with the prior behaviour) — idempotency
                    // therefore keys on the already-Refunded state.
                    if (cancelledInvoice.Status == InvoiceStatus.Refunded)
                    {
                        logger.LogInformation(
                            "Montonio webhook: invoice {InvoiceId} already refunded/voided — duplicate {Status} for ref {Ref}, ignoring",
                            cancelledInvoice.Id, status, ref_);
                        return true;
                    }

                    if (status == "cancelled" && cancelledInvoice.Status != InvoiceStatus.AwaitingPayment)
                    {
                        // A 'cancelled' (abandoned-checkout) event for an invoice that is not
                        // awaiting payment (e.g. already Paid) is stale/out-of-order — ignore it
                        // so a successful payment is never downgraded to Refunded.
                        logger.LogWarning(
                            "Montonio webhook: ignoring stale 'cancelled' for invoice {InvoiceId} in status {Status} (ref {Ref})",
                            cancelledInvoice.Id, cancelledInvoice.Status, ref_);
                        return true;
                    }

                    if (status == "refunded" && cancelledInvoice.Status != InvoiceStatus.Paid)
                    {
                        // A genuine refund can only follow a Paid invoice. A 'refunded' event for a
                        // non-Paid invoice is out-of-order/unexpected — do not cancel the booking.
                        logger.LogWarning(
                            "Montonio webhook: ignoring 'refunded' for invoice {InvoiceId} in non-Paid status {Status} (ref {Ref})",
                            cancelledInvoice.Id, cancelledInvoice.Status, ref_);
                        return true;
                    }

                    cancelledInvoice.Status = status == "refunded"
                        ? InvoiceStatus.Refunded
                        : InvoiceStatus.Refunded; // cancelled order = treat as voided/refunded

                    if (cancelledInvoice.Booking is not null
                        && cancelledInvoice.Booking.Status != BookingStatus.Cancelled)
                    {
                        cancelledInvoice.Booking.Status    = BookingStatus.Cancelled;
                        cancelledInvoice.Booking.UpdatedAt = DateTime.UtcNow;
                    }

                    // Reverse the supplier ledger: cancel the Order and its pending
                    // PayoutEntry so a refunded/voided booking never pays the supplier.
                    var refundedOrder = await db.Orders
                        .FirstOrDefaultAsync(o => o.BookingId == cancelledInvoice.BookingId);
                    if (refundedOrder is not null)
                    {
                        if (refundedOrder.Status != OrderStatus.Cancelled)
                        {
                            refundedOrder.Status    = OrderStatus.Cancelled;
                            refundedOrder.UpdatedAt = DateTime.UtcNow;
                        }
                        var refundedPayout = await db.PayoutEntries
                            .FirstOrDefaultAsync(p => p.OrderId == refundedOrder.Id
                                                   && p.Status == PayoutStatus.Pending);
                        if (refundedPayout is not null)
                            refundedPayout.Status = PayoutStatus.Cancelled;
                    }

                    await db.SaveChangesAsync();

                    SentrySdk.CaptureMessage(
                        $"Montonio payment {status} — invoice {cancelledInvoice.Id} voided, booking cancelled",
                        scope =>
                        {
                            scope.Level = SentryLevel.Info;
                            scope.SetExtra("invoiceId",         cancelledInvoice.Id.ToString());
                            scope.SetExtra("bookingId",         cancelledInvoice.BookingId.ToString());
                            scope.SetExtra("merchantReference", ref_);
                            scope.SetExtra("montonioStatus",    status);
                        });
                }
                else
                {
                    logger.LogWarning(
                        "Montonio webhook: {Status} for unknown merchant_reference {Ref}",
                        status, ref_);
                }

                return true;
            }

            if (status != "paid")
            {
                logger.LogWarning(
                    "Montonio webhook: unexpected status '{Status}' for ref={Ref}",
                    status, ref_);
                SentrySdk.CaptureMessage(
                    $"Unknown Montonio payment status: {status}",
                    scope =>
                    {
                        scope.Level = SentryLevel.Warning;
                        scope.SetExtra("merchantReference", ref_);
                        scope.SetExtra("status",            status);
                    });
                return false;
            }

            var invoice = await db.Invoices
                .FirstOrDefaultAsync(i => i.PaymentOrderId == ref_);

            if (invoice is null)
            {
                logger.LogWarning(
                    "Montonio webhook: no invoice found for merchant_reference {Ref}",
                    ref_);
                return false;
            }

            // Idempotency guard — if already processed, return success immediately.
            if (invoice.Status == InvoiceStatus.Paid)
            {
                logger.LogInformation(
                    "Montonio webhook: invoice {InvoiceId} already paid — duplicate webhook for ref {Ref}, ignoring",
                    invoice.Id, ref_);
                return true;
            }

            // Terminal-refund guard (symmetric to the refund branch's guard): a late
            // 'paid' event for an invoice that was already refunded/voided — e.g. the
            // customer cancelled during the PSP redirect window, so booking/order/payout
            // are already Cancelled — must NOT silently re-mark it Paid. That would record
            // money as collected against a dead booking with no refund trigger. Escalate
            // for manual refund reconciliation instead.
            if (invoice.Status is InvoiceStatus.Refunded or InvoiceStatus.PendingRefund)
            {
                logger.LogWarning(
                    "Montonio webhook: 'paid' event for already-refunded/cancelled invoice {InvoiceId} (ref {Ref}, status {Status}) — NOT marking paid; manual refund reconciliation required",
                    invoice.Id, ref_, invoice.Status);
                SentrySdk.CaptureMessage(
                    "Montonio 'paid' webhook for refunded/cancelled invoice — manual refund required",
                    scope =>
                    {
                        scope.Level = SentryLevel.Warning;
                        scope.SetExtra("invoiceId",         invoice.Id.ToString());
                        scope.SetExtra("merchantReference", ref_);
                        scope.SetExtra("invoiceStatus",     invoice.Status.ToString());
                    });
                return true; // ack so Montonio stops retrying; reconciliation is manual
            }

            // Amount/currency verification — never mark an invoice Paid on a webhook whose
            // reported total or currency does not match what we billed. The 'data' claim is
            // already signature-verified, but a mismatch still indicates a tampered/wrong-order
            // or misconfigured event; refuse rather than under/over-charge silently.
            if (webhookAmount is null
                || webhookCurrency is null
                || webhookAmount.Value != invoice.Amount
                || !string.Equals(webhookCurrency, "EUR", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "Montonio webhook: amount/currency mismatch for invoice {InvoiceId} (ref {Ref}). "
                    + "Webhook reported {WebhookAmount} {WebhookCurrency}, invoice expects {InvoiceAmount} EUR — refusing to mark paid",
                    invoice.Id, ref_, webhookAmount, webhookCurrency, invoice.Amount);
                SentrySdk.CaptureMessage(
                    "Montonio webhook amount/currency mismatch — payment not applied",
                    scope =>
                    {
                        scope.Level = SentryLevel.Warning;
                        scope.SetExtra("invoiceId",         invoice.Id.ToString());
                        scope.SetExtra("merchantReference", ref_);
                        scope.SetExtra("webhookAmount",     webhookAmount?.ToString() ?? "null");
                        scope.SetExtra("webhookCurrency",   webhookCurrency ?? "null");
                        scope.SetExtra("invoiceAmount",     invoice.Amount.ToString());
                    });
                return false;
            }

            invoice.Status = InvoiceStatus.Paid;
            invoice.PaidAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            logger.LogInformation(
                "Invoice {InvoiceId} paid via Montonio. BookingId={BookingId} Amount={Amount}",
                invoice.Id, invoice.BookingId, invoice.Amount);

            // Payment confirmed — now dispatch order to supplier
            var order = await db.Orders
                .FirstOrDefaultAsync(o => o.BookingId == invoice.BookingId);

            if (order is not null && order.AutoDispatch
                && order.Status == OrderStatus.Created)
            {
                order.Status    = OrderStatus.Sending;
                order.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();

                BackgroundJob.Enqueue<BackgroundOrderDispatchService>(
                    x => x.DispatchOrderAsync(order.Id));

                logger.LogInformation(
                    "Payment confirmed for booking {BookingId} — dispatching order {OrderId}",
                    invoice.BookingId, order.Id);
            }
            else if (order is not null && !order.AutoDispatch)
            {
                // Needs manual approval — admins were already notified at booking creation
                logger.LogInformation(
                    "Payment confirmed for booking {BookingId} — order {OrderId} awaits admin approval",
                    invoice.BookingId, order.Id);
            }
            else if (order is null)
            {
                logger.LogWarning(
                    "Payment confirmed for booking {BookingId} but no Order found — manual intervention required",
                    invoice.BookingId);
                SentrySdk.CaptureMessage(
                    $"Montonio payment confirmed but no Order exists for booking {invoice.BookingId}",
                    scope =>
                    {
                        scope.SetExtra("bookingId",         invoice.BookingId.ToString());
                        scope.SetExtra("invoiceId",         invoice.Id.ToString());
                        scope.SetExtra("merchantReference", ref_);
                    },
                    SentryLevel.Warning);
            }

            return true;
        }
        catch (SecurityTokenException ex)
        {
            logger.LogWarning(ex,
                "Montonio webhook JWT verification failed. merchantReference={Ref}",
                merchantReference);
            // Use CaptureMessage (not CaptureException) — a bad JWT is a warning-level
            // event (possible replay attack or misconfiguration), not an application error.
            // Include only the first 20 chars of the token to aid debugging without leaking data.
            SentrySdk.CaptureMessage(
                "Montonio webhook JWT validation failed",
                scope =>
                {
                    scope.Level = SentryLevel.Warning;
                    scope.SetExtra("tokenPrefix",       token.Length > 20 ? token[..20] : token);
                    scope.SetExtra("merchantReference", merchantReference ?? "unknown");
                    scope.SetExtra("exceptionMessage",  ex.Message);
                });
            // Throw so the controller can return 400 — a bad JWT should not receive a 200
            // (which would suppress Montonio retries for a request that was never legitimate).
            throw new WebhookSignatureException("Montonio webhook JWT validation failed.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Montonio webhook processing failed. merchantReference={Ref}",
                merchantReference);
            SentrySdk.CaptureException(ex, scope =>
                scope.SetExtra("merchantReference", merchantReference ?? "unknown"));
            return false;
        }
    }

    // Montonio returns { "payment_url": "..." } — map both casing variants.
    private record MontonioOrderResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("payment_url")]
        string? PaymentUrl);
}
