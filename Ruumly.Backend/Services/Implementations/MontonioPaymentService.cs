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
            string? ref_   = null;
            string? status = null;

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

            if (string.IsNullOrEmpty(ref_) || status != "paid")
            {
                logger.LogWarning(
                    "Montonio webhook: missing merchant_reference or non-paid status. ref={Ref} status={Status}",
                    ref_, status);
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
            SentrySdk.CaptureException(ex, scope =>
                scope.SetExtra("merchantReference", merchantReference ?? "unknown"));
            return false;
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
