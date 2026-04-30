using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class OrderService(
    RuumlyDbContext db,
    IIntegrationDispatchService dispatchService,
    INotificationService notificationService,
    IInvoiceService invoiceService,
    IEmailSender emailSender,
    IConfiguration config,
    ILogger<OrderService> logger,
    IHttpContextAccessor http) : IOrderService
{
    private string Lang => http.HttpContext?.Request.GetLang() ?? "et";
    private string Msg(string key) => ErrorMessages.Get(key, Lang);

    // ─── Queries ──────────────────────────────────────────────────────────────

    public async Task<PaginatedResult<OrderDto>> GetAllAsync(Guid userId, UserRole role, int page = 1, int limit = 50, Guid? supplierId = null)
    {
        page  = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);

        var query = db.Orders
            .Include(o => o.Supplier)
            .Include(o => o.FulfillmentEvents)
            .Include(o => o.Timeline)
            .Include(o => o.Booking)
            .AsSplitQuery()
            .AsQueryable();

        if (role == UserRole.Provider)
        {
            var user = await db.Users.FindAsync(userId);
            if (user is not null)
            {
                if (user.SupplierId.HasValue)
                    query = query.Where(o => o.SupplierId == user.SupplierId.Value);
                else
                    query = query.Where(o => o.Supplier.ContactEmail == user.Email);
            }
        }
        else if (role == UserRole.Customer)
        {
            query = query.Where(o => o.Booking.UserId == userId);
        }
        else if (role == UserRole.Admin && supplierId.HasValue)
        {
            query = query.Where(o => o.SupplierId == supplierId.Value);
        }

        var total = await query.CountAsync();

        // Admin inbox: surface high-priority suppliers first
        var sorted = role == UserRole.Admin
            ? query.OrderByDescending(o => (int)o.Supplier!.PriorityLevel)
                   .ThenByDescending(o => o.CreatedAt)
            : query.OrderByDescending(o => o.CreatedAt);

        var orders = await sorted
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

        var data = orders.Select(MapToDto).ToList();
        return new PaginatedResult<OrderDto>(data, total, page, limit, (page - 1) * limit + data.Count < total);
    }

    public async Task<OrderDto?> GetByIdAsync(Guid id)
    {
        var order = await LoadOrder(id);
        return order is null ? null : MapToDto(order);
    }

    public async Task<OrderDto?> GetByBookingIdAsync(Guid bookingId)
    {
        var order = await db.Orders
            .Include(o => o.Supplier)
            .Include(o => o.FulfillmentEvents.OrderBy(e => e.CreatedAt))
            .Include(o => o.Timeline.OrderBy(t => t.CreatedAt))
            .Include(o => o.Booking)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.BookingId == bookingId);

        return order is null ? null : MapToDto(order);
    }

    // ─── Approve ──────────────────────────────────────────────────────────────

    public async Task<OrderDto> ApproveAsync(Guid id, Guid approvedByUserId)
    {
        var order = await LoadOrder(id)
            ?? throw new NotFoundException(Msg("ORDER_NOT_FOUND"));

        if (order.Status != OrderStatus.Created && order.Status != OrderStatus.Sending)
            throw new ArgumentException(Msg("ORDER_WRONG_STATUS"));

        // Gate: invoice must be paid before dispatching — unless payment method is "later"
        var invoice = await db.Invoices
            .FirstOrDefaultAsync(i => i.BookingId == order.BookingId);
        if (invoice is not null
            && invoice.PaymentMethod != "later"
            && invoice.Status != InvoiceStatus.Paid)
            throw new ArgumentException("Cannot dispatch — invoice not paid yet.");

        var approver = await db.Users.FindAsync(approvedByUserId);
        var approverName = approver?.Name ?? approvedByUserId.ToString();

        var tl = EmailTranslations.For((await db.Users.FindAsync(order.Booking?.UserId))?.Language);

        order.Status     = OrderStatus.Sent;
        order.ApprovedBy = approverName;
        order.ApprovedAt = DateTime.UtcNow;
        order.UpdatedAt  = DateTime.UtcNow;

        db.FulfillmentEvents.Add(new FulfillmentEvent
        {
            Id        = Guid.NewGuid(),
            OrderId   = order.Id,
            Status    = FulfillmentStatus.Approved,
            Actor     = approverName,
            ActorRole = approver?.Role ?? UserRole.Admin,
            Detail    = $"Approved: {approverName}",
            CreatedAt = DateTime.UtcNow,
        });

        db.OrderTimelines.Add(new OrderTimeline
        {
            Id        = Guid.NewGuid(),
            OrderId   = order.Id,
            Event     = tl.TimelineOrderApproved,
            Status    = OrderStatus.Sent,
            Detail    = $"Approver: {approverName}",
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();

        // For API/Email: trigger automatic dispatch.
        // For Manual: the approval itself IS the notification — skip re-dispatch.
        if (order.Supplier.IntegrationType != Models.Enums.IntegrationType.Manual)
            await dispatchService.DispatchAsync(order, order.Supplier);

        // Update linked booking
        var booking = await db.Bookings
            .Include(b => b.Listing)
            .FirstOrDefaultAsync(b => b.Id == order.BookingId);

        if (booking is not null)
        {
            booking.Status    = BookingStatus.Confirmed;
            booking.UpdatedAt = DateTime.UtcNow;

            db.BookingTimelines.Add(new BookingTimeline
            {
                Id        = Guid.NewGuid(),
                BookingId = booking.Id,
                Event     = tl.TimelinePartnerConfirmed,
                Status    = BookingStatus.Confirmed,
                CreatedAt = DateTime.UtcNow,
            });

            await NotifyBookingStatusAsync(
                booking,
                notificationTitle: tl.NotifBookingConfirmed,
                notificationBody:  $"{booking.Listing?.Title ?? order.ListingTitle} — {tl.NotifBookingConfirmedBody}",
                emailSubject:      tl.BookingStatusConfirmedSubject,
                emailBody:         tl.BookingStatusConfirmedBody);

            await db.SaveChangesAsync();
        }

        // Reload for fresh DTO
        var fresh = await LoadOrder(id);
        return MapToDto(fresh!);
    }

    // ─── Reject ───────────────────────────────────────────────────────────────

    public async Task<OrderDto> RejectAsync(Guid id, string reason, Guid rejectedByUserId)
    {
        var order = await LoadOrder(id)
            ?? throw new NotFoundException(Msg("ORDER_NOT_FOUND"));

        var rejecter = await db.Users.FindAsync(rejectedByUserId);
        var rejecterName = rejecter?.Name ?? rejectedByUserId.ToString();

        var tl = EmailTranslations.For((await db.Users.FindAsync(order.Booking?.UserId))?.Language);

        order.Status    = OrderStatus.Rejected;
        order.Notes     = string.IsNullOrWhiteSpace(reason) ? order.Notes : reason;
        order.UpdatedAt = DateTime.UtcNow;

        db.FulfillmentEvents.Add(new FulfillmentEvent
        {
            Id        = Guid.NewGuid(),
            OrderId   = order.Id,
            Status    = FulfillmentStatus.Rejected,
            Actor     = rejecterName,
            ActorRole = rejecter?.Role ?? UserRole.Admin,
            Detail    = reason,
            CreatedAt = DateTime.UtcNow,
        });

        db.OrderTimelines.Add(new OrderTimeline
        {
            Id        = Guid.NewGuid(),
            OrderId   = order.Id,
            Event     = tl.TimelineOrderRejected,
            Status    = OrderStatus.Rejected,
            Detail    = reason,
            CreatedAt = DateTime.UtcNow,
        });

        var booking = await db.Bookings
            .Include(b => b.Listing)
            .FirstOrDefaultAsync(b => b.Id == order.BookingId);

        if (booking is not null)
        {
            booking.Status    = BookingStatus.Cancelled;
            booking.UpdatedAt = DateTime.UtcNow;

            db.BookingTimelines.Add(new BookingTimeline
            {
                Id        = Guid.NewGuid(),
                BookingId = booking.Id,
                Event     = tl.TimelineBookingCancelled,
                Status    = BookingStatus.Cancelled,
                CreatedAt = DateTime.UtcNow,
            });

            await NotifyBookingStatusAsync(
                booking,
                notificationTitle: tl.NotifBookingRejected,
                notificationBody:  string.IsNullOrWhiteSpace(reason) ? tl.NotifBookingRejectedBody : reason,
                emailSubject:      tl.BookingStatusRejectedSubject,
                emailBody:         tl.BookingStatusRejectedBody);
        }

        await db.SaveChangesAsync();

        var fresh = await LoadOrder(id);
        return MapToDto(fresh!);
    }

    // ─── Confirm (webhook / admin manual) ────────────────────────────────────

    public async Task<OrderDto> ConfirmAsync(Guid id, Guid confirmedByUserId)
    {
        var order = await LoadOrder(id)
            ?? throw new NotFoundException(Msg("ORDER_NOT_FOUND"));

        var confirmer     = await db.Users.FindAsync(confirmedByUserId);
        var confirmerName = confirmer?.Name ?? "Partner";

        var tl = EmailTranslations.For((await db.Users.FindAsync(order.Booking?.UserId))?.Language);

        order.Status      = OrderStatus.Confirmed;
        order.ConfirmedAt = DateTime.UtcNow;
        order.UpdatedAt   = DateTime.UtcNow;

        db.FulfillmentEvents.Add(new FulfillmentEvent
        {
            Id        = Guid.NewGuid(),
            OrderId   = order.Id,
            Status    = FulfillmentStatus.Confirmed,
            Actor     = confirmerName,
            ActorRole = confirmer?.Role ?? UserRole.Provider,
            Detail    = $"Confirmed: {confirmerName}",
            CreatedAt = DateTime.UtcNow,
        });

        db.OrderTimelines.Add(new OrderTimeline
        {
            Id        = Guid.NewGuid(),
            OrderId   = order.Id,
            Event     = tl.TimelinePartnerConfirmed,
            Status    = OrderStatus.Confirmed,
            Detail    = $"Confirmed: {confirmerName}",
            CreatedAt = DateTime.UtcNow,
        });

        var booking = await db.Bookings
            .Include(b => b.Listing)
            .FirstOrDefaultAsync(b => b.Id == order.BookingId);

        if (booking is not null)
        {
            booking.Status    = BookingStatus.Active;
            booking.UpdatedAt = DateTime.UtcNow;

            db.BookingTimelines.Add(new BookingTimeline
            {
                Id        = Guid.NewGuid(),
                BookingId = booking.Id,
                Event     = tl.TimelineServiceActive,
                Status    = BookingStatus.Active,
                CreatedAt = DateTime.UtcNow,
            });

            await NotifyBookingStatusAsync(
                booking,
                notificationTitle: tl.NotifBookingConfirmed,
                notificationBody:  $"{booking.Listing?.Title ?? order.ListingTitle} — {tl.NotifServiceActiveBody}",
                emailSubject:      tl.BookingStatusConfirmedSubject,
                emailBody:         tl.BookingStatusConfirmedBody);

            // Auto-generate invoice (idempotent — skips if already exists)
            await invoiceService.GenerateAsync(booking.Id);
        }

        await db.SaveChangesAsync();

        var fresh = await LoadOrder(id);
        return MapToDto(fresh!);
    }

    // ─── Admin status override ────────────────────────────────────────────────

    public async Task<OrderDto> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request)
    {
        var order = await LoadOrder(id)
            ?? throw new NotFoundException(Msg("ORDER_NOT_FOUND"));

        if (!Enum.TryParse<OrderStatus>(request.Status, ignoreCase: true, out var newStatus))
            throw new ArgumentException(Msg("ORDER_WRONG_STATUS"));

        var tl = EmailTranslations.For((await db.Users.FindAsync(order.Booking?.UserId))?.Language);

        order.Status    = newStatus;
        order.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.Notes))
            order.Notes = request.Notes;

        if (!string.IsNullOrWhiteSpace(request.ApprovedBy))
        {
            order.ApprovedBy = request.ApprovedBy;
            order.ApprovedAt = DateTime.UtcNow;
        }

        db.OrderTimelines.Add(new OrderTimeline
        {
            Id        = Guid.NewGuid(),
            OrderId   = order.Id,
            Event     = $"{tl.TimelineStatusChanged}: {newStatus}",
            Status    = newStatus,
            Detail    = request.Notes,
            CreatedAt = DateTime.UtcNow,
        });

        // Sync booking status and notify customer for terminal transitions
        if (newStatus is OrderStatus.Completed or OrderStatus.Cancelled)
        {
            var booking = await db.Bookings
                .Include(b => b.Listing)
                .FirstOrDefaultAsync(b => b.Id == order.BookingId);

            if (booking is not null)
            {
                booking.Status    = newStatus == OrderStatus.Completed
                    ? BookingStatus.Completed
                    : BookingStatus.Cancelled;
                booking.UpdatedAt = DateTime.UtcNow;

                db.BookingTimelines.Add(new BookingTimeline
                {
                    Id        = Guid.NewGuid(),
                    BookingId = booking.Id,
                    Event     = newStatus == OrderStatus.Completed
                        ? tl.TimelineServiceCompleted
                        : tl.TimelineBookingCancelled,
                    Status    = booking.Status,
                    CreatedAt = DateTime.UtcNow,
                });

                var (notifTitle, notifBody, emailSubject, emailBody) = newStatus == OrderStatus.Completed
                    ? (tl.TimelineServiceCompleted,
                       $"{booking.Listing?.Title ?? order.ListingTitle} — {tl.NotifServiceCompletedBody}",
                       tl.BookingStatusCompletedSubject,
                       tl.BookingStatusCompletedBody)
                    : (tl.NotifBookingCancelled,
                       $"{booking.Listing?.Title ?? order.ListingTitle} — {tl.NotifBookingCancelledBody}",
                       tl.BookingStatusCancelledSubject,
                       tl.BookingStatusCancelledBody);

                await NotifyBookingStatusAsync(
                    booking,
                    notifTitle, notifBody, emailSubject, emailBody);
            }
        }

        await db.SaveChangesAsync();

        var fresh = await LoadOrder(id);
        return MapToDto(fresh!);
    }

    // ─── Booking status notification + email ─────────────────────────────────

    private async Task NotifyBookingStatusAsync(
        Booking booking,
        string notificationTitle,
        string notificationBody,
        string emailSubject,
        string emailBody)
    {
        var shortId = booking.Id.ToString()[..8].ToUpper();
        // Templates already include the "#" sigil before {id}; pass the raw id only.
        var subject = emailSubject.Replace("{id}", shortId);
        var body    = emailBody.Replace("{id}", shortId);

        // In-app notification
        await notificationService.CreateAsync(
            booking.UserId,
            NotificationType.Booking,
            notificationTitle,
            notificationBody,
            actionUrl:  "/account?tab=bookings",
            entityId:   booking.Id.ToString(),
            entityType: "Booking");

        // Email to contact address — never let failure break the status transition
        if (!string.IsNullOrWhiteSpace(booking.ContactEmail))
        {
            try
            {
                var accountUrl = $"{config["AppUrl"]}/account?tab=bookings";
                var user = await db.Users.FindAsync(booking.UserId);
                var t    = EmailTranslations.For(user?.Language);

                var textBody =
                    $"Tere {booking.ContactName},\n\n" +
                    $"{body}\n\n" +
                    $"{t.BookingStatusViewLink}: {accountUrl}\n\n" +
                    $"Ruumly\ninfo@ruumly.eu";

                await emailSender.SendAsync(booking.ContactEmail, subject, textBody);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to send booking status email to {Email} for booking {Id}",
                    booking.ContactEmail, booking.Id);
            }
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private Task<Order?> LoadOrder(Guid id) =>
        db.Orders
            .Include(o => o.Supplier)
            .Include(o => o.FulfillmentEvents.OrderBy(e => e.CreatedAt))
            .Include(o => o.Timeline.OrderBy(t => t.CreatedAt))
            .Include(o => o.Booking)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id);

    public OrderDto MapToDto(Order o) => MapOrderToDto(o);

    private static OrderDto MapOrderToDto(Order o) => new(
        Id:               o.Id,
        BookingId:        o.BookingId,
        ListingId:        o.ListingId,
        SupplierId:       o.SupplierId,
        SupplierName:     o.Supplier?.Name ?? string.Empty,
        ListingTitle:     o.ListingTitle,
        ListingType:      o.ListingType.ToString().ToLower(),
        IntegrationType:  o.IntegrationType.ToString().ToLower(),
        CustomerName:     o.CustomerName,
        CustomerEmail:    o.CustomerEmail,
        CustomerPhone:    o.CustomerPhone,
        City:             o.City,
        StartDate:        o.StartDate.ToString("yyyy-MM-dd"),
        EndDate:          o.EndDate?.ToString("yyyy-MM-dd"),
        Duration:         o.Duration,
        Extras:           o.ExtrasKeys,
        BasePrice:        o.BasePrice,
        PlatformPrice:    o.PlatformPrice,
        SupplierPrice:    o.SupplierPrice,
        ExtrasTotal:      o.ExtrasTotal,
        Total:            o.Total,
        Margin:           o.Margin,
        Status:           o.Status.ToString().ToLower(),
        ApprovedBy:       o.ApprovedBy,
        ApprovedAt:       o.ApprovedAt?.ToString("yyyy-MM-dd HH:mm"),
        PostingChannel:   o.PostingChannel?.ToString().ToLower(),
        SentAt:           o.SentAt?.ToString("yyyy-MM-dd HH:mm"),
        ConfirmedAt:      o.ConfirmedAt?.ToString("yyyy-MM-dd HH:mm"),
        Notes:            o.Notes,
        CreatedAt:        o.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
        LeadStatus:       o.LeadStatus.ToString().ToLower(),
        LastContactAt:    o.LastContactAt?.ToString("yyyy-MM-dd HH:mm"),
        ProviderNotes:    o.ProviderNotes,
        Timeline: o.Timeline
            .Select(t => new OrderTimelineDto(
                Date:   t.CreatedAt.ToString("yyyy-MM-dd"),
                Time:   t.CreatedAt.ToString("HH:mm"),
                Event:  t.Event,
                Status: t.Status.ToString().ToLower(),
                Detail: t.Detail
            )).ToList(),
        FulfillmentEvents: o.FulfillmentEvents
            .Select(e => new FulfillmentEventDto(
                Id:        e.Id,
                Status:    e.Status.ToString().ToLower(),
                Actor:     e.Actor,
                Channel:   e.Channel?.ToString().ToLower(),
                Detail:    e.Detail,
                CreatedAt: e.CreatedAt.ToString("yyyy-MM-dd HH:mm")
            )).ToList()
    );
}
