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
        // Honor order-level posting channel (set by routing service from IntegrationSettings)
        // Fall back to supplier's base integration type if PostingChannel is null
        var channel = order.PostingChannel ?? (PostingMode)(int)supplier.IntegrationType;

        switch (channel)
        {
            case PostingMode.Api:
                await DispatchApiAsync(order, supplier);
                break;

            case PostingMode.Email:
                await DispatchEmailAsync(order, supplier);
                break;

            case PostingMode.Manual:
                await DispatchManualAsync(order);
                break;
        }
    }

    // ─── API dispatch ─────────────────────────────────────────────────────────

    private async Task DispatchApiAsync(Order order, Supplier supplier)
    {
        var settings = await db.IntegrationSettings
            .FirstOrDefaultAsync(s => s.SupplierId == supplier.Id);
        var fallback = settings?.FallbackPostingMode ?? PostingMode.Email;

        if (string.IsNullOrWhiteSpace(supplier.ApiEndpoint))
        {
            logger.LogWarning("Supplier {SupplierId} has no API endpoint. Falling back to {Fallback}.", supplier.Id, fallback);
            if (fallback == PostingMode.Email)
                await DispatchEmailAsync(order, supplier);
            else
                await DispatchManualAsync(order);
            return;
        }

        var client = httpClientFactory.CreateClient();

        // Decrypt stored token before use in HTTP header
        string? plainToken = null;
        if (!string.IsNullOrWhiteSpace(supplier.ApiAuthToken))
        {
            try
            {
                plainToken = tokenProtector.Unprotect(supplier.ApiAuthToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to decrypt ApiAuthToken for supplier {SupplierId}. Token may be plain-text legacy value.",
                    supplier.Id);
            }
        }
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
                logger.LogWarning("API dispatch failed for order {OrderId}: {StatusCode}. Falling back to {Fallback}.", order.Id, statusCode, fallback);

                db.FulfillmentEvents.Add(new FulfillmentEvent
                {
                    Id        = Guid.NewGuid(),
                    OrderId   = order.Id,
                    Status    = FulfillmentStatus.Failed,
                    Actor     = "system",
                    ActorRole = UserRole.Admin,
                    Channel   = PostingMode.Api,
                    Detail    = $"POST {supplier.ApiEndpoint} → {statusCode} (failed, falling back to {fallback})",
                    CreatedAt = DateTime.UtcNow,
                });

                await db.SaveChangesAsync();
                if (fallback == PostingMode.Email)
                    await DispatchEmailAsync(order, supplier);
                else
                    await DispatchManualAsync(order);
                return;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "API dispatch exception for order {OrderId}. Falling back to {Fallback}.", order.Id, fallback);

            db.FulfillmentEvents.Add(new FulfillmentEvent
            {
                Id        = Guid.NewGuid(),
                OrderId   = order.Id,
                Status    = FulfillmentStatus.Failed,
                Actor     = "system",
                ActorRole = UserRole.Admin,
                Channel   = PostingMode.Api,
                Detail    = $"Exception: {ex.Message} — falling back to {fallback}",
                CreatedAt = DateTime.UtcNow,
            });

            await db.SaveChangesAsync();
            if (fallback == PostingMode.Email)
                await DispatchEmailAsync(order, supplier);
            else
                await DispatchManualAsync(order);
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
        if (order.ExtrasSnapshot.Count > 0)
        {
            sb.AppendLine("Lisateenused:");
            foreach (var extra in order.ExtrasSnapshot)
                sb.AppendLine($"  • {extra.Label,-20} €{extra.SupplierPrice:F2}");
        }
        sb.AppendLine();
        sb.AppendLine("═══════════════════════════════════");
        sb.AppendLine("HIND");
        sb.AppendLine("═══════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine($"Partneri hind:    €{order.SupplierPrice:F2}");
        if (order.ExtrasSnapshot.Count > 0)
        {
            var extrasSupplierTotal = order.ExtrasSnapshot.Sum(e => e.SupplierPrice);
            sb.AppendLine($"Lisateenused:     €{extrasSupplierTotal:F2}");
            sb.AppendLine($"Kokku partnerile: €{order.SupplierPrice + extrasSupplierTotal:F2}");
        }
        else
        {
            sb.AppendLine($"Kokku partnerile: €{order.SupplierPrice:F2}");
        }

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
