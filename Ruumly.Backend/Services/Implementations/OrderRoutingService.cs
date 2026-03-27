using Hangfire;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class OrderRoutingService(
    RuumlyDbContext db,
    INotificationService notificationService,
    IPricingConfigService pricingConfigService,
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
        // Supplier price = base price minus negotiated partner discount
        var partnerDiscountRate = supplier.PartnerDiscountRate;
        if (partnerDiscountRate == 0)
        {
            var config = await pricingConfigService.GetAsync();
            partnerDiscountRate = config.DefaultPartnerDiscountRate;
        }
        var supplierPrice = Math.Round(booking.BasePrice * (1m - partnerDiscountRate / 100m));

        // Extras supplier total (from the snapshot stored on the booking)
        var extrasSupplierTotal = booking.ExtrasSnapshot.Sum(e => e.SupplierPrice);
        var extrasCustomerTotal = booking.ExtrasTotal;  // what customer paid for extras

        // Margin = (what customer paid for base) - (what supplier gets for base)
        //        + (what customer paid for extras) - (what supplier gets for extras)
        var baseMargin   = booking.PlatformPrice - supplierPrice;
        var extrasMargin = extrasCustomerTotal - extrasSupplierTotal;
        var margin       = baseMargin + extrasMargin;

        if (margin < 0)
            logger.LogWarning(
                "Negative margin on booking {BookingId}: base margin {BaseMargin}, " +
                "extras margin {ExtrasMargin}. PartnerDiscount={PD}%, CustomerDiscount={CD}%",
                booking.Id, baseMargin, extrasMargin,
                partnerDiscountRate, booking.PlatformPrice / booking.BasePrice * 100);

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
            ExtrasSnapshot  = booking.ExtrasSnapshot,
            IntegrationType = supplier.IntegrationType,
            CustomerName    = booking.ContactName ?? string.Empty,
            CustomerEmail   = booking.ContactEmail ?? string.Empty,
            CustomerPhone   = booking.ContactPhone ?? string.Empty,
            BasePrice       = booking.BasePrice,
            PlatformPrice   = booking.PlatformPrice,
            SupplierPrice   = supplierPrice,
            ExtrasTotal     = extrasCustomerTotal,  // customer-facing total
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
            BackgroundJob.Enqueue<BackgroundOrderDispatchService>(
                x => x.DispatchOrderAsync(order.Id));
        }
        else
        {
            // Notify admins that approval is needed
            var admins = await db.Users
                .Where(u => u.Role == UserRole.Admin)
                .ToListAsync();

            foreach (var admin in admins)
            {
                await notificationService.CreateAsync(
                    admin.Id,
                    NotificationType.Order,
                    "Uus tellimus vajab kinnitust",
                    $"Tellimus teenusele \"{listing.Title}\" ootab kinnitust",
                    actionUrl:  $"/orders/{order.Id}",
                    entityId:   order.Id.ToString(),
                    entityType: "Order");
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
