using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class OrderRoutingService(
    RuumlyDbContext db,
    IIntegrationDispatchService dispatchService,
    ILogger<OrderRoutingService> logger) : IOrderRoutingService
{
    public async Task RouteOrderAsync(Booking booking, Listing listing)
    {
        // 1. Load active routing rules ordered by priority (highest first)
        var rules = await db.OrderRoutingRules
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.Priority)
            .ToListAsync();

        // 2. Find first matching rule
        var matchedRule = rules.FirstOrDefault(r =>
            (r.ServiceType == null || r.ServiceType == listing.Type) &&
            (r.PriceThreshold == null || booking.Total >= r.PriceThreshold));

        // 3. Load supplier with IntegrationSettings
        var supplier = await db.Suppliers
            .Include(s => s.IntegrationSettings)
            .FirstOrDefaultAsync(s => s.Id == booking.SupplierId);

        if (supplier is null)
        {
            logger.LogWarning("Supplier {SupplierId} not found for booking {BookingId}. Skipping order routing.", booking.SupplierId, booking.Id);
            return;
        }

        // 4. Calculate supplier price and margin
        var supplierPrice = Math.Round(booking.BasePrice * 0.85m);
        var margin        = booking.Total - supplierPrice - booking.ExtrasTotal;

        // 5. Determine posting channel
        var postingChannel = matchedRule?.PostingChannel
            ?? (PostingMode)(int)supplier.IntegrationType;

        var approvalMode = supplier.IntegrationSettings?.ApprovalMode ?? ApprovalMode.Admin;

        // 6. Create Order entity
        var order = new Order
        {
            Id              = Guid.NewGuid(),
            BookingId       = booking.Id,
            SupplierId      = supplier.Id,
            ListingId       = listing.Id,
            ListingTitle    = listing.Title,
            ListingType     = listing.Type,
            City            = listing.City,
            StartDate       = booking.StartDate,
            EndDate         = booking.EndDate,
            Duration        = booking.Duration,
            Extras          = booking.Extras,
            IntegrationType = supplier.IntegrationType,
            CustomerName    = booking.ContactName ?? string.Empty,
            CustomerEmail   = booking.ContactEmail ?? string.Empty,
            CustomerPhone   = booking.ContactPhone ?? string.Empty,
            BasePrice       = booking.BasePrice,
            PlatformPrice   = booking.PlatformPrice,
            SupplierPrice   = supplierPrice,
            ExtrasTotal     = booking.ExtrasTotal,
            Total           = booking.Total,
            Margin          = margin,
            ApprovalMode    = approvalMode,
            PostingChannel  = postingChannel,
            Notes           = booking.Notes ?? string.Empty,
            Status          = OrderStatus.Created,
            CreatedAt       = DateTime.UtcNow,
            UpdatedAt       = DateTime.UtcNow,
        };

        db.Orders.Add(order);

        // 7. Initial fulfillment event
        db.FulfillmentEvents.Add(new FulfillmentEvent
        {
            Id        = Guid.NewGuid(),
            OrderId   = order.Id,
            Status    = FulfillmentStatus.AwaitingApproval,
            Actor     = "system",
            ActorRole = UserRole.Admin,
            Detail    = "Tellimus loodud, suunamine alustatud",
            CreatedAt = DateTime.UtcNow,
        });

        // 8. Initial order timeline entry
        db.OrderTimelines.Add(new OrderTimeline
        {
            Id        = Guid.NewGuid(),
            OrderId   = order.Id,
            Event     = "Tellimus loodud",
            Status    = OrderStatus.Created,
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();

        // 9. Dispatch or queue for approval
        bool autoDispatch = !matchedRule?.RequiresApproval == true || approvalMode == ApprovalMode.Auto;

        if (autoDispatch)
        {
            await dispatchService.DispatchAsync(order, supplier);
        }
        else
        {
            // Notify admins that approval is needed
            var admins = await db.Users
                .Where(u => u.Role == UserRole.Admin)
                .ToListAsync();

            foreach (var admin in admins)
            {
                db.Notifications.Add(new Notification
                {
                    Id        = Guid.NewGuid(),
                    UserId    = admin.Id,
                    Type      = NotificationType.Order,
                    Title     = "Uus tellimus vajab kinnitust",
                    Desc      = $"Tellimus teenusele \"{listing.Title}\" ootab kinnitust",
                    ActionUrl = $"/orders/{order.Id}",
                    EntityId  = order.Id.ToString(),
                    EntityType = "Order",
                    Channel   = NotificationChannel.InApp,
                    CreatedAt = DateTime.UtcNow,
                });
            }

            order.Status    = OrderStatus.Sending;
            order.UpdatedAt = DateTime.UtcNow;

            db.OrderTimelines.Add(new OrderTimeline
            {
                Id        = Guid.NewGuid(),
                OrderId   = order.Id,
                Event     = "Ootame kinnitust",
                Status    = OrderStatus.Sending,
                Detail    = "Tellimus vajab käsitsi kinnitust enne saatmist",
                CreatedAt = DateTime.UtcNow,
            });

            await db.SaveChangesAsync();
        }
    }
}
