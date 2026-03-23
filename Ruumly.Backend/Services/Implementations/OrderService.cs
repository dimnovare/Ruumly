using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
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
    IInvoiceService invoiceService) : IOrderService
{
    // ─── Queries ──────────────────────────────────────────────────────────────

    public async Task<List<OrderDto>> GetAllAsync(Guid userId, UserRole role)
    {
        var query = db.Orders
            .Include(o => o.Supplier)
            .Include(o => o.FulfillmentEvents)
            .Include(o => o.Timeline)
            .Include(o => o.Booking)
            .AsQueryable();

        if (role == UserRole.Provider)
        {
            // Match by supplier whose contact email equals the logged-in user's email
            var user = await db.Users.FindAsync(userId);
            if (user is not null)
                query = query.Where(o => o.Supplier.ContactEmail == user.Email);
        }
        else if (role == UserRole.Customer)
        {
            query = query.Where(o => o.Booking.UserId == userId);
        }
        // Admin: no filter

        var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();
        return orders.Select(MapToDto).ToList();
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
            .FirstOrDefaultAsync(o => o.BookingId == bookingId);

        return order is null ? null : MapToDto(order);
    }

    // ─── Approve ──────────────────────────────────────────────────────────────

    public async Task<OrderDto> ApproveAsync(Guid id, Guid approvedByUserId)
    {
        var order = await LoadOrder(id)
            ?? throw new NotFoundException($"Order {id} not found.");

        if (order.Status != OrderStatus.Created && order.Status != OrderStatus.Sending)
            throw new ArgumentException($"Order is in status '{order.Status}' and cannot be approved.");

        var approver = await db.Users.FindAsync(approvedByUserId);
        var approverName = approver?.Name ?? approvedByUserId.ToString();

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
            Detail    = $"Kinnitatud: {approverName}",
            CreatedAt = DateTime.UtcNow,
        });

        db.OrderTimelines.Add(new OrderTimeline
        {
            Id        = Guid.NewGuid(),
            OrderId   = order.Id,
            Event     = "Tellimus kinnitatud",
            Status    = OrderStatus.Sent,
            Detail    = $"Kinnitaja: {approverName}",
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
                Event     = "Partner kinnitas",
                Status    = BookingStatus.Confirmed,
                CreatedAt = DateTime.UtcNow,
            });

            await notificationService.CreateAsync(
                booking.UserId,
                NotificationType.Booking,
                "Broneering kinnitatud",
                $"{booking.Listing?.Title ?? order.ListingTitle} on kinnitatud",
                actionUrl:  "/account?tab=bookings",
                entityId:   booking.Id.ToString(),
                entityType: "Booking");

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
            ?? throw new NotFoundException($"Order {id} not found.");

        var rejecter = await db.Users.FindAsync(rejectedByUserId);
        var rejecterName = rejecter?.Name ?? rejectedByUserId.ToString();

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
            Event     = "Tellimus tagasi lükatud",
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
                Event     = "Broneering tühistatud",
                Status    = BookingStatus.Cancelled,
                CreatedAt = DateTime.UtcNow,
            });

            await notificationService.CreateAsync(
                booking.UserId,
                NotificationType.Booking,
                "Broneering tagasi lükatud",
                string.IsNullOrWhiteSpace(reason) ? "Teie broneering lükati tagasi" : reason,
                actionUrl:  "/account?tab=bookings",
                entityId:   booking.Id.ToString(),
                entityType: "Booking");
        }

        await db.SaveChangesAsync();

        var fresh = await LoadOrder(id);
        return MapToDto(fresh!);
    }

    // ─── Confirm (webhook / admin manual) ────────────────────────────────────

    public async Task<OrderDto> ConfirmAsync(Guid id)
    {
        var order = await LoadOrder(id)
            ?? throw new NotFoundException($"Order {id} not found.");

        order.Status      = OrderStatus.Confirmed;
        order.ConfirmedAt = DateTime.UtcNow;
        order.UpdatedAt   = DateTime.UtcNow;

        db.FulfillmentEvents.Add(new FulfillmentEvent
        {
            Id        = Guid.NewGuid(),
            OrderId   = order.Id,
            Status    = FulfillmentStatus.Confirmed,
            Actor     = "system",
            ActorRole = UserRole.Admin,
            Detail    = "Partner kinnitas tellimuse",
            CreatedAt = DateTime.UtcNow,
        });

        db.OrderTimelines.Add(new OrderTimeline
        {
            Id        = Guid.NewGuid(),
            OrderId   = order.Id,
            Event     = "Partner kinnitas tellimuse",
            Status    = OrderStatus.Confirmed,
            Detail    = "Automaatne kinnitus",
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
                Event     = "Teenus on aktiivne",
                Status    = BookingStatus.Active,
                CreatedAt = DateTime.UtcNow,
            });

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
            ?? throw new NotFoundException($"Order {id} not found.");

        if (!Enum.TryParse<OrderStatus>(request.Status, ignoreCase: true, out var newStatus))
            throw new ArgumentException($"Invalid status '{request.Status}'.");

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
            Event     = $"Staatus muudetud: {newStatus}",
            Status    = newStatus,
            Detail    = request.Notes,
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();

        var fresh = await LoadOrder(id);
        return MapToDto(fresh!);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private Task<Order?> LoadOrder(Guid id) =>
        db.Orders
            .Include(o => o.Supplier)
            .Include(o => o.FulfillmentEvents.OrderBy(e => e.CreatedAt))
            .Include(o => o.Timeline.OrderBy(t => t.CreatedAt))
            .Include(o => o.Booking)
            .FirstOrDefaultAsync(o => o.Id == id);

    private static OrderDto MapToDto(Order o) => new(
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
        Extras:           o.Extras,
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
