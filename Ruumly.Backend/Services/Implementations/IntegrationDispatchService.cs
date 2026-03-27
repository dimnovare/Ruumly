using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class IntegrationDispatchService(
    RuumlyDbContext db,
    IHttpClientFactory httpClientFactory,
    IEmailSender emailSender,
    INotificationService notificationService,
    ILogger<IntegrationDispatchService> logger,
    TokenProtector tokenProtector) : IIntegrationDispatchService
{
    public async Task DispatchAsync(Order order, Supplier supplier)
    {
        switch (supplier.IntegrationType)
        {
            case IntegrationType.Api:
                await DispatchApiAsync(order, supplier);
                break;

            case IntegrationType.Email:
                await DispatchEmailAsync(order, supplier);
                break;

            case IntegrationType.Manual:
                await DispatchManualAsync(order);
                break;
        }
    }

    // ─── API dispatch ─────────────────────────────────────────────────────────

    private async Task DispatchApiAsync(Order order, Supplier supplier)
    {
        if (string.IsNullOrWhiteSpace(supplier.ApiEndpoint))
        {
            logger.LogWarning("Supplier {SupplierId} has no API endpoint. Falling back to email.", supplier.Id);
            await DispatchEmailAsync(order, supplier);
            return;
        }

        var client = httpClientFactory.CreateClient();

        // Unprotect stored token before use in HTTP header
        var plainToken = tokenProtector.Unprotect(supplier.ApiAuthToken);
        if (!string.IsNullOrWhiteSpace(plainToken))
        {
            if (string.Equals(supplier.ApiAuthType, "bearer", StringComparison.OrdinalIgnoreCase))
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", plainToken);
            else if (string.Equals(supplier.ApiAuthType, "apikey", StringComparison.OrdinalIgnoreCase))
                client.DefaultRequestHeaders.Add("X-API-Key", plainToken);
        }

        var payload = new
        {
            orderId       = order.Id,
            listingTitle  = order.ListingTitle,
            listingType   = order.ListingType.ToString().ToLower(),
            startDate     = order.StartDate.ToString("yyyy-MM-dd"),
            endDate       = order.EndDate?.ToString("yyyy-MM-dd"),
            duration      = order.Duration,
            extras        = order.ExtrasKeys,
            customerName  = order.CustomerName,
            customerEmail = order.CustomerEmail,
            customerPhone = order.CustomerPhone,
            supplierPrice = order.SupplierPrice,
            extrasTotal   = order.ExtrasTotal,
            notes         = order.Notes,
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        try
        {
            var response = await client.PostAsync(supplier.ApiEndpoint, content);
            var statusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                order.Status    = OrderStatus.Sent;
                order.SentAt    = DateTime.UtcNow;
                order.PostingChannel = PostingMode.Api;
                order.UpdatedAt = DateTime.UtcNow;

                db.FulfillmentEvents.Add(new FulfillmentEvent
                {
                    Id        = Guid.NewGuid(),
                    OrderId   = order.Id,
                    Status    = FulfillmentStatus.Posted,
                    Actor     = "system",
                    ActorRole = UserRole.Admin,
                    Channel   = PostingMode.Api,
                    Detail    = $"POST {supplier.ApiEndpoint} → {statusCode}",
                    CreatedAt = DateTime.UtcNow,
                });

                db.OrderTimelines.Add(new OrderTimeline
                {
                    Id        = Guid.NewGuid(),
                    OrderId   = order.Id,
                    Event     = "Saadetud API kaudu",
                    Status    = OrderStatus.Sent,
                    Detail    = $"POST {supplier.ApiEndpoint} → {statusCode}",
                    CreatedAt = DateTime.UtcNow,
                });
            }
            else
            {
                logger.LogWarning("API dispatch failed for order {OrderId}: {StatusCode}. Falling back to email.", order.Id, statusCode);

                db.FulfillmentEvents.Add(new FulfillmentEvent
                {
                    Id        = Guid.NewGuid(),
                    OrderId   = order.Id,
                    Status    = FulfillmentStatus.Failed,
                    Actor     = "system",
                    ActorRole = UserRole.Admin,
                    Channel   = PostingMode.Api,
                    Detail    = $"POST {supplier.ApiEndpoint} → {statusCode} (failed, falling back to email)",
                    CreatedAt = DateTime.UtcNow,
                });

                await db.SaveChangesAsync();
                await DispatchEmailAsync(order, supplier);
                return;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "API dispatch exception for order {OrderId}. Falling back to email.", order.Id);

            db.FulfillmentEvents.Add(new FulfillmentEvent
            {
                Id        = Guid.NewGuid(),
                OrderId   = order.Id,
                Status    = FulfillmentStatus.Failed,
                Actor     = "system",
                ActorRole = UserRole.Admin,
                Channel   = PostingMode.Api,
                Detail    = $"Exception: {ex.Message} — falling back to email",
                CreatedAt = DateTime.UtcNow,
            });

            await db.SaveChangesAsync();
            await DispatchEmailAsync(order, supplier);
            return;
        }

        await db.SaveChangesAsync();
    }

    // ─── Email dispatch ───────────────────────────────────────────────────────

    private async Task DispatchEmailAsync(Order order, Supplier supplier)
    {
        var recipient = supplier.RecipientEmail ?? supplier.ContactEmail;
        var subject   = $"Ruumly: Uus tellimus #{order.Id.ToString()[..8]}";
        var body      = BuildEmailBody(order, supplier.Name);

        await emailSender.SendAsync(recipient, subject, body);

        order.Status         = OrderStatus.Sent;
        order.SentAt         = DateTime.UtcNow;
        order.PostingChannel = PostingMode.Email;
        order.UpdatedAt      = DateTime.UtcNow;

        db.FulfillmentEvents.Add(new FulfillmentEvent
        {
            Id        = Guid.NewGuid(),
            OrderId   = order.Id,
            Status    = FulfillmentStatus.Posted,
            Actor     = "system",
            ActorRole = UserRole.Admin,
            Channel   = PostingMode.Email,
            Detail    = $"E-kiri saadetud: {recipient}",
            CreatedAt = DateTime.UtcNow,
        });

        db.OrderTimelines.Add(new OrderTimeline
        {
            Id        = Guid.NewGuid(),
            OrderId   = order.Id,
            Event     = "Tellimus saadetud e-postiga",
            Status    = OrderStatus.Sent,
            Detail    = $"E-kiri saadetud: {recipient}",
            CreatedAt = DateTime.UtcNow,
        });

        db.OrderTimelines.Add(new OrderTimeline
        {
            Id        = Guid.NewGuid(),
            OrderId   = order.Id,
            Event     = "Ootame partneri kinnitust",
            Status    = OrderStatus.Sent,
            CreatedAt = DateTime.UtcNow.AddSeconds(1),
        });

        await db.SaveChangesAsync();
    }

    // ─── Manual dispatch ──────────────────────────────────────────────────────

    private async Task DispatchManualAsync(Order order)
    {
        order.Status         = OrderStatus.Sending;
        order.PostingChannel = PostingMode.Manual;
        order.UpdatedAt      = DateTime.UtcNow;

        db.FulfillmentEvents.Add(new FulfillmentEvent
        {
            Id        = Guid.NewGuid(),
            OrderId   = order.Id,
            Status    = FulfillmentStatus.Posting,
            Actor     = "system",
            ActorRole = UserRole.Admin,
            Channel   = PostingMode.Manual,
            Detail    = "Manuaalne integratsioon — operaator peab partneri teavitama",
            CreatedAt = DateTime.UtcNow,
        });

        db.OrderTimelines.Add(new OrderTimeline
        {
            Id        = Guid.NewGuid(),
            OrderId   = order.Id,
            Event     = "Ootame operaatori tegevust",
            Status    = OrderStatus.Sending,
            Detail    = "Manuaalne integratsioon — operaator peab partneri teavitama",
            CreatedAt = DateTime.UtcNow,
        });

        // Notify all admins
        var admins = await db.Users
            .Where(u => u.Role == UserRole.Admin)
            .ToListAsync();

        foreach (var admin in admins)
        {
            await notificationService.CreateAsync(
                admin.Id,
                NotificationType.Order,
                "Manuaalne tellimus vajab edastamist",
                $"Tellimus {order.Id} — {order.ListingTitle} vajab manuaalset edastamist",
                actionUrl:  $"/orders/{order.Id}",
                entityId:   order.Id.ToString(),
                entityType: "Order");
        }

        await db.SaveChangesAsync();
    }

    // ─── Email body builder (mirrors generateOrderEmailPreview from mockOrders.ts) ──

    private static string BuildEmailBody(Order order, string supplierName)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Tere, {supplierName}!");
        sb.AppendLine();
        sb.AppendLine("Ruumly platvormilt on saabunud uus tellimus.");
        sb.AppendLine();
        sb.AppendLine("═══════════════════════════════════");
        sb.AppendLine("TELLIMUSE ANDMED");
        sb.AppendLine("═══════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine($"Tellimuse nr:    {order.Id}");
        sb.AppendLine($"Teenus:          {order.ListingTitle}");
        var typeLabel = order.ListingType switch
        {
            ListingType.Warehouse => "Laopind",
            ListingType.Moving    => "Kolimine",
            ListingType.Trailer   => "Haagise rent",
            _                     => order.ListingType.ToString(),
        };
        sb.AppendLine($"Tüüp:           {typeLabel}");
        sb.AppendLine();
        sb.AppendLine("═══════════════════════════════════");
        sb.AppendLine("KLIENT");
        sb.AppendLine("═══════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine($"Nimi:            {order.CustomerName}");
        sb.AppendLine($"E-post:          {order.CustomerEmail}");
        sb.AppendLine($"Telefon:         {order.CustomerPhone}");
        sb.AppendLine();
        sb.AppendLine("═══════════════════════════════════");
        sb.AppendLine("DETAILID");
        sb.AppendLine("═══════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine($"Alguskuupäev:    {order.StartDate:yyyy-MM-dd}");
        if (order.EndDate.HasValue)
            sb.AppendLine($"Lõppkuupäev:     {order.EndDate:yyyy-MM-dd}");
        sb.AppendLine($"Periood:         {order.Duration}");
        if (order.ExtrasKeys.Count > 0)
            sb.AppendLine($"Lisateenused:    {string.Join(", ", order.ExtrasKeys)}");
        sb.AppendLine();
        sb.AppendLine("═══════════════════════════════════");
        sb.AppendLine("HIND");
        sb.AppendLine("═══════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine($"Partneri hind:   €{order.SupplierPrice}");
        if (order.ExtrasTotal > 0)
            sb.AppendLine($"Lisateenused:    €{order.ExtrasTotal}");
        sb.AppendLine($"Kokku partnerile: €{order.SupplierPrice + order.ExtrasTotal}");

        if (!string.IsNullOrWhiteSpace(order.Notes))
        {
            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════");
            sb.AppendLine("MÄRKUSED");
            sb.AppendLine("═══════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine(order.Notes);
        }

        sb.AppendLine();
        sb.AppendLine("═══════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine("Palun kinnitage tellimus 2 tunni jooksul.");
        sb.AppendLine();
        sb.AppendLine("Kinnitamiseks vastake sellele e-kirjale märksõnaga KINNITAN");
        sb.AppendLine("või logige sisse Ruumly partneripaneeli.");
        sb.AppendLine();
        sb.AppendLine("Lugupidamisega,");
        sb.AppendLine("Ruumly meeskond");
        sb.AppendLine("info@ruumly.eu | +372 5555 1234");

        return sb.ToString();
    }
}
