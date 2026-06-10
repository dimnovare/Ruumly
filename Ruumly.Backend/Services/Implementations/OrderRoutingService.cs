using Hangfire;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
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

        // 4. Load pricing config and calculate supplier price via PricingHelpers
        // — same discount cascade as BookingService and ListingService;
        //   honours listing.ClientDiscountRateOverride to prevent margin drift
        var pricingConfig = await pricingConfigService.GetAsync();
        var (partnerDiscountRate, _) = PricingHelpers.ComputeEffectiveDiscounts(
            listingPartnerOverride:  listing.PartnerDiscountRateOverride,
            listingCustomerOverride: listing.ClientDiscountRateOverride,
            supplierPartnerRate:     supplier.PartnerDiscountRate,
            defaultPartnerDiscount:  pricingConfig.DefaultPartnerDiscountRate,
            ruumlyMinMargin:         pricingConfig.RuumlyMinMarginRate);
        var supplierPrice = Math.Round(booking.BasePrice * (1m - partnerDiscountRate / 100m), 2);

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
                partnerDiscountRate,
                booking.BasePrice > 0
                    ? Math.Round((1m - booking.PlatformPrice / booking.BasePrice) * 100m, 1)
                    : 0m);

        // 5. Determine posting channel
        // If IntegrationSettings exists and is deactivated, force Manual mode.
        // The order is still created so admin sees it in dashboard, but no
        // forwarding happens until admin reactivates the integration.
        var integrationActive = supplier.IntegrationSettings?.IsActive ?? true;

        var postingChannel = !integrationActive
            ? PostingMode.Manual
            : matchedRule?.PostingChannel
              ?? (PostingMode)(int)supplier.IntegrationType;

        var approvalMode = supplier.IntegrationSettings?.ApprovalMode ?? ApprovalMode.Admin;

        if (!integrationActive)
        {
            logger.LogInformation(
                "Integration deactivated for supplier {SupplierId}. Order will be created with Manual channel.",
                supplier.Id);
        }

        // 6. Create Order entity
        var order = new Order
        {
            Id              = Guid.NewGuid(),
            BookingId       = booking.Id,
            SupplierId      = supplier.Id,
            ListingId       = listing.Id,
            ListingTitle    = listing.Title,
            ListingType     = listing.Type,
            City            = listing.Location?.City ?? listing.City,
            StartDate       = booking.StartDate,
            EndDate         = booking.EndDate,
            Duration        = booking.Duration,
            ExtrasSnapshot  = booking.ExtrasSnapshot,
            PartnerDiscountRate = partnerDiscountRate,
            AutoDispatch    = approvalMode == ApprovalMode.Auto
                              || matchedRule == null
                              || !matchedRule.RequiresApproval,
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

        db.PayoutEntries.Add(new PayoutEntry
        {
            Id             = Guid.NewGuid(),
            SupplierId     = supplier.Id,
            OrderId        = order.Id,
            SupplierAmount = supplierPrice + extrasSupplierTotal,
            PlatformMargin = margin,
            Status         = PayoutStatus.Pending,
        });

        // Resolve language for timeline/notification strings
        var bookingUser = await db.Users.FindAsync(booking.UserId);
        var tl          = EmailTranslations.For(bookingUser?.Language);

        // 7. Initial fulfillment event
        db.FulfillmentEvents.Add(new FulfillmentEvent
        {
            Id        = Guid.NewGuid(),
            OrderId   = order.Id,
            Status    = FulfillmentStatus.AwaitingApproval,
            Actor     = "system",
            ActorRole = UserRole.Admin,
            Detail    = "Order created, routing started",
            CreatedAt = DateTime.UtcNow,
        });

        // 8. Initial order timeline entry
        db.OrderTimelines.Add(new OrderTimeline
        {
            Id        = Guid.NewGuid(),
            OrderId   = order.Id,
            Event     = tl.TimelineBookingCreated,
            Status    = OrderStatus.Created,
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();

        // For rebate model: dispatch immediately (no payment through Ruumly)
        // For marketplace model: dispatch is triggered by payment webhook
        if (supplier.BillingModel == BillingModel.Rebate && order.AutoDispatch)
        {
            BackgroundJob.Enqueue<BackgroundOrderDispatchService>(
                x => x.DispatchOrderAsync(order.Id));
        }

        // Notify admins if manual approval is needed before dispatch
        if (!order.AutoDispatch)
        {
            var admins = await db.Users
                .Where(u => u.Role == UserRole.Admin)
                .ToListAsync();

            foreach (var admin in admins)
            {
                await notificationService.CreateAsync(
                    admin.Id,
                    NotificationType.Order,
                    "New order requires approval",
                    $"Order for \"{listing.Title}\" awaiting confirmation",
                    actionUrl:  $"/orders/{order.Id}",
                    entityId:   order.Id.ToString(),
                    entityType: "Order");
            }

            db.OrderTimelines.Add(new OrderTimeline
            {
                Id        = Guid.NewGuid(),
                OrderId   = order.Id,
                Event     = tl.TimelineAwaitingApproval,
                Status    = OrderStatus.Created,
                Detail    = tl.TimelineManualApprovalNeeded,
                CreatedAt = DateTime.UtcNow,
            });

            await db.SaveChangesAsync();
        }
    }
}
