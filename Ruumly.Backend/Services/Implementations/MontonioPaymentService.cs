using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

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
    private string ApiUrl =>
        config["Montonio:ApiUrl"]
            ?? "https://api.montonio.com";
    private string ReturnUrl =>
        config["Montonio:ReturnUrl"] ?? "";
    private string NotifyUrl =>
        config["Montonio:NotifyUrl"] ?? "";

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
                System.Text.Json.JsonSerializer
                    .Serialize(payload))],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: creds);
        var jwtString = new JwtSecurityTokenHandler()
            .WriteToken(jwt);

        var http = httpFactory.CreateClient();
        var res  = await http.PostAsJsonAsync(
            $"{ApiUrl}/merchant/payment-orders",
            new { data = jwtString });

        if (!res.IsSuccessStatusCode)
        {
            logger.LogError(
                "Montonio order creation failed: {S}",
                res.StatusCode);
            throw new InvalidOperationException(
                ErrorMessages.Get("PAYMENT_PROVIDER_UNAVAILABLE",
                    httpAccessor.HttpContext?.Request.GetLang() ?? "et"));
        }

        var body = await res.Content
            .ReadFromJsonAsync<MontonioOrderResponse>()
            ?? throw new InvalidOperationException(
                "Invalid Montonio response");

        invoice.PaymentOrderId = orderId;
        invoice.PaymentMethod  = paymentMethod;
        invoice.Status = InvoiceStatus.AwaitingPayment;
        await db.SaveChangesAsync();

        return body.PaymentUrl;
    }

    public async Task<bool> HandleWebhookAsync(
        string token)
    {
        try
        {
            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(SecretKey));
            var handler =
                new JwtSecurityTokenHandler();
            handler.ValidateToken(token,
                new TokenValidationParameters
                {
                    ValidateIssuer   = false,
                    ValidateAudience = false,
                    IssuerSigningKey = key,
                    ClockSkew        = TimeSpan.Zero,
                }, out var validated);

            var jwt   = (JwtSecurityToken)validated;
            var ref_  = jwt.Claims.FirstOrDefault(
                c => c.Type == "merchant_reference")
                ?.Value;
            var status = jwt.Claims.FirstOrDefault(
                c => c.Type == "payment_status")
                ?.Value;

            if (string.IsNullOrEmpty(ref_)
                || status != "paid")
                return false;

            var invoice = await db.Invoices
                .FirstOrDefaultAsync(i =>
                    i.PaymentOrderId == ref_);
            if (invoice is null) return false;

            invoice.Status = InvoiceStatus.Paid;
            invoice.PaidAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            logger.LogInformation(
                "Invoice {Id} paid via Montonio",
                invoice.Id);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Montonio webhook failed");
            return false;
        }
    }

    private record MontonioOrderResponse(
        string PaymentUrl);
}
