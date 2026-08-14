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

    public async Task<PaginatedResult<OrderDto>> GetAllAsync(Guid userId, UserRole role, int page = 1, int limit = 50, Guid? supplierId = null, string? status = null, CancellationToken ct = default)
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
            var user = await db.Users
                .Where(u => u.Id == userId)
                .Select(u => new { u.SupplierId, u.Email })
                .FirstOrDefaultAsync(ct);
            if (user is not null)
            {
                if (user.SupplierId.HasValue)
                    query = query.Where(o => o.SupplierId == user.SupplierId.Value);
                else
                {
                    // Legacy fallback for a provider with no SupplierId FK. Matched
                    // case-insensitively because the two columns are filled by
                    // different hands — the login is stored canonical, the supplier
                    // row keeps whatever casing ops typed or the import carried.
                    // A blank address matches nothing: imported directory rows store
                    // ContactEmail as "" when the source had none.
                    var email = EmailValidation.Normalize(user.Email);
                    query = email.Length > 0
                        ? query.Where(o => o.Supplier.ContactEmail.ToLower() == email)
                        : query.Where(o => false);
                }
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

        // Optional server-side status filter (AdminOrders tabs). Unknown/blank → no filter.
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var statusFilter))
            query = query.Where(o => o.Status == statusFilter);

        var total = await query.CountAsync(ct);

        // Admin inbox: surface high-priority suppliers first
        var sorted = role == UserRole.Admin
            ? query.OrderByDescending(o => (int)o.Supplier!.PriorityLevel)
                   .ThenByDescending(o => o.CreatedAt)
            : query.OrderByDescending(o => o.CreatedAt);

        var orders = await sorted
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync(ct);

        // Customers get a redacted projection: never expose margin, supplier price,
        // posting channel or raw fulfillment/timeline detail (partner endpoints).
        var redact = role == UserRole.Customer;
        var data = orders.Select(o => redact ? Redact(MapToDto(o)) : MapToDto(o)).ToList();
        return new PaginatedResult<OrderDto>(data, total, page, limit, (page - 1) * limit + data.Count < total);
    }

    /// <summary>
    /// Order counts grouped by status. Provider/Customer are scoped to their own
    /// data; an Admin sees all orders, or one supplier's when supplierId is given.
    /// Powers the AdminOrders status tabs without pulling every order client-side.
    /// Keys are lowercased status names plus an "all" total.
    /// Scoping uses scalar sub-queries (no navigation-property access) so it is
    /// reliably SQL-translatable and works under the InMemory test provider.
    /// </summary>
    public async Task<Dictionary<string, int>> GetStatusCountsAsync(Guid userId, UserRole role, Guid? supplierId = null, CancellationToken ct = default)
    {
        var query = db.Orders.AsNoTracking().AsQueryable();

        if (role == UserRole.Provider)
        {
            var user = await db.Users
                .Where(u => u.Id == userId)
                .Select(u => new { u.SupplierId, u.Email })
                .FirstOrDefaultAsync(ct);
            if (user is not null)
            {
                if (user.SupplierId.HasValue)
                    query = query.Where(o => o.SupplierId == user.SupplierId.Value);
                else
                {
                    var email = EmailValidation.Normalize(user.Email);
                    List<Guid> supplierIds = email.Length == 0
                        ? []
                        : await db.Suppliers
                            .Where(s => s.ContactEmail.ToLower() == email)
                            .Select(s => s.Id)
                            .ToListAsync(ct);
                    query = query.Where(o => supplierIds.Contains(o.SupplierId));
                }
            }
        }
        else if (role == UserRole.Customer)
        {
            query = query.Where(o => db.Bookings.Any(b => b.Id == o.BookingId && b.UserId == userId));
        }
        else if (role == UserRole.Admin && supplierId.HasValue)
        {
            query = query.Where(o => o.SupplierId == supplierId.Value);
        }

        var grouped = await query
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var counts = grouped.ToDictionary(x => x.Status.ToString().ToLowerInvariant(), x => x.Count);
        counts["all"] = grouped.Sum(x => x.Count);
        return counts;
    }

    public async Task<OrderDto?> GetByIdAsync(Guid id, Guid callerId, UserRole callerRole, CancellationToken ct = default)
    {
        var order = await LoadOrder(id, ct);
        if (order is null) return null;

        // IDOR guard — mirror GetByBookingIdAsync: Admin always passes; a Provider may only
        // read their own supplier's order; a Customer may only read their own booking's order.
        // Use the not-found message so callers can't enumerate other suppliers' order IDs.
        if (callerRole == UserRole.Customer && order.Booking?.UserId != callerId)
            throw new ForbiddenException(Msg("ORDER_NOT_FOUND"));

        if (callerRole == UserRole.Provider)
        {
            var callerSupplierId = await db.Users
                .Where(u => u.Id == callerId)
                .Select(u => u.SupplierId)
                .FirstOrDefaultAsync(ct);
            if (callerSupplierId != order.SupplierId)
                throw new ForbiddenException(Msg("ORDER_NOT_FOUND"));
        }

        var dto = MapToDto(order);
        return callerRole == UserRole.Customer ? Redact(dto) : dto;
    }

    public async Task<OrderDto?> GetByBookingIdAsync(Guid bookingId, Guid callerId, UserRole callerRole, CancellationToken ct = default)
    {
        var order = await db.Orders
            .Include(o => o.Supplier)
            .Include(o => o.FulfillmentEvents.OrderBy(e => e.CreatedAt))
            .Include(o => o.Timeline.OrderBy(t => t.CreatedAt))
            .Include(o => o.Booking)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.BookingId == bookingId, ct);

        if (order is null) return null;

        // Ownership check — Admin always passes; same message as not-found to avoid enumeration
        if (callerRole == UserRole.Customer && order.Booking?.UserId != callerId)
            throw new ForbiddenException(Msg("ORDER_NOT_FOUND"));

        if (callerRole == UserRole.Provider)
        {
            var callerSupplierId = await db.Users
                .Where(u => u.Id == callerId)
                .Select(u => u.SupplierId)
                .FirstOrDefaultAsync(ct);
            if (callerSupplierId != order.SupplierId)
                throw new ForbiddenException(Msg("ORDER_NOT_FOUND"));
        }

        var dto = MapToDto(order);
        return callerRole == UserRole.Customer ? Redact(dto) : dto;
    }

    // ─── Approve ──────────────────────────────────────────────────────────────

    public async Task<OrderDto> ApproveAsync(Guid id, Guid approvedByUserId, CancellationToken ct = default)
    {
        var order = await LoadOrder(id, ct)
            ?? throw new NotFoundException(Msg("ORDER_NOT_FOUND"));

        // Ownership check — Provider can only act on their own supplier's orders
        var approver = await db.Users
            .Where(u => u.Id == approvedByUserId)
            .Select(u => new { u.Name, u.Role, u.SupplierId })
            .FirstOrDefaultAsync(ct);
        if (approver?.Role == UserRole.Provider &&
            approver.SupplierId != order.SupplierId)
            throw new ForbiddenException(Msg("ORDER_NOT_FOUND"));

        if (order.Status != OrderStatus.Created && order.Status != OrderStatus.Sending)
            throw new ArgumentException(Msg("ORDER_WRONG_STATUS"));

        // Gate: invoice must be paid before dispatching — unless payment method is "later"
        var invoice = await db.Invoices
            .FirstOrDefaultAsync(i => i.BookingId == order.BookingId, ct);
        if (invoice is not null
            && invoice.PaymentMethod != "later"
            && invoice.Status != InvoiceStatus.Paid)
            throw new ArgumentException(Msg("INVOICE_NOT_PAID"));


        var approverName = approver?.Name ?? approvedByUserId.ToString();

        var tl = EmailTranslations.For(order.Booking?.User?.Language);

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

        await db.SaveChangesAsync(ct);

        // For API/Email: trigger automatic dispatch.
        // For Manual: the approval itself IS the notification — skip re-dispatch.
        if (order.Supplier.IntegrationType != Models.Enums.IntegrationType.Manual)
            await dispatchService.DispatchAsync(order, order.Supplier);

        // Update linked booking
        var booking = await db.Bookings
            .Include(b => b.Listing)
            .Include(b => b.User)
            .FirstOrDefaultAsync(b => b.Id == order.BookingId, ct);

        if (booking is not null)
        {
            // Approval FORWARDS the request to the partner — it is NOT yet a partner
            // confirmation. We keep the booking out of the auto-expire window (status
            // Confirmed, not Reserved — BackgroundCleanupService voids stale Reserved
            // bookings), but we record an honest "approved & sent" timeline event and
            // deliberately do NOT send the customer a "booking confirmed" notification
            // here. The real confirmation — and the customer-facing confirmed email —
            // fires in ConfirmAsync when the partner actually acknowledges (webhook,
            // poll, or admin/provider "Mark confirmed").
            booking.Status    = BookingStatus.AwaitingConfirmation;
            booking.UpdatedAt = DateTime.UtcNow;

            db.BookingTimelines.Add(new BookingTimeline
            {
                Id        = Guid.NewGuid(),
                BookingId = booking.Id,
                Event     = tl.TimelineOrderApproved,
                Status    = BookingStatus.AwaitingConfirmation,
                CreatedAt = DateTime.UtcNow,
            });

            await db.SaveChangesAsync(ct);
        }

        // Reload for fresh DTO
        var fresh = await LoadOrder(id, ct);
        return MapToDto(fresh!);
    }

    // ─── Reject ───────────────────────────────────────────────────────────────

    public async Task<OrderDto> RejectAsync(Guid id, string reason, Guid rejectedByUserId, CancellationToken ct = default)
    {
        var order = await LoadOrder(id, ct)
            ?? throw new NotFoundException(Msg("ORDER_NOT_FOUND"));

        // Ownership check — Provider can only act on their own supplier's orders
        var rejecter = await db.Users
            .Where(u => u.Id == rejectedByUserId)
            .Select(u => new { u.Name, u.Role, u.SupplierId })
            .FirstOrDefaultAsync(ct);
        if (rejecter?.Role == UserRole.Provider &&
            rejecter.SupplierId != order.SupplierId)
            throw new ForbiddenException(Msg("ORDER_NOT_FOUND"));

        var rejecterName = rejecter?.Name ?? rejectedByUserId.ToString();

        var tl = EmailTranslations.For(order.Booking?.User?.Language);

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
            .Include(b => b.User)
            .FirstOrDefaultAsync(b => b.Id == order.BookingId, ct);

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

        await db.SaveChangesAsync(ct);

        var fresh = await LoadOrder(id, ct);
        return MapToDto(fresh!);
    }

    // ─── Confirm (webhook / admin manual) ────────────────────────────────────

    public async Task<OrderDto> ConfirmAsync(Guid id, Guid confirmedByUserId, CancellationToken ct = default)
    {
        var order = await LoadOrder(id, ct)
            ?? throw new NotFoundException(Msg("ORDER_NOT_FOUND"));

        // Ownership check — Provider can only act on their own supplier's orders
        var confirmer = await db.Users
            .Where(u => u.Id == confirmedByUserId)
            .Select(u => new { u.Name, u.Role, u.SupplierId })
            .FirstOrDefaultAsync(ct);
        if (confirmer?.Role == UserRole.Provider &&
            confirmer.SupplierId != order.SupplierId)
            throw new ForbiddenException(Msg("ORDER_NOT_FOUND"));

        var confirmerName = confirmer?.Name ?? "Partner";

        // Terminal-state guard: a cancelled/rejected/completed order must not be
        // resurrected to Confirmed/Active (e.g. confirming an already-refunded
        // booking). Mirrors the guard in ApproveAsync.
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Rejected or OrderStatus.Completed)
            throw new ArgumentException(Msg("ORDER_WRONG_STATUS"));

        var tl = EmailTranslations.For(order.Booking?.User?.Language);

        order.Status      = OrderStatus.Confirmed;
        order.ConfirmedAt = DateTime.UtcNow;
        order.UpdatedAt   = DateTime.UtcNow;

        // Fulfillment confirmed: the payout Ruumly provisioned at order-creation now
        // becomes a real obligation. Promote Accrued -> Pending so it enters the
        // payable queue (AdminPayoutsController) and counts toward rebate margin.
        var accruedPayouts = await db.PayoutEntries
            .Where(p => p.OrderId == order.Id && p.Status == PayoutStatus.Accrued)
            .ToListAsync(ct);
        foreach (var payout in accruedPayouts)
            payout.Status = PayoutStatus.Pending;

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
            .Include(b => b.User)
            .FirstOrDefaultAsync(b => b.Id == order.BookingId, ct);

        if (booking is not null)
        {
            // Never re-activate a finalised booking (cancelled/refunded or completed).
            if (booking.Status is BookingStatus.Cancelled or BookingStatus.Completed)
                throw new ArgumentException(Msg("ORDER_WRONG_STATUS"));

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

        await db.SaveChangesAsync(ct);

        var fresh = await LoadOrder(id, ct);
        return MapToDto(fresh!);
    }

    // ─── Admin status override ────────────────────────────────────────────────

    public async Task<OrderDto> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request, CancellationToken ct = default)
    {
        var order = await LoadOrder(id, ct)
            ?? throw new NotFoundException(Msg("ORDER_NOT_FOUND"));

        if (!Enum.TryParse<OrderStatus>(request.Status, ignoreCase: true, out var newStatus))
            throw new ArgumentException(Msg("ORDER_WRONG_STATUS"));

        // Don't allow transitions OUT of a terminal state (cancelled/rejected/
        // completed) — admin override must not re-open a refunded/closed order.
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Rejected or OrderStatus.Completed)
            throw new ArgumentException(Msg("ORDER_WRONG_STATUS"));

        var tl = EmailTranslations.For(order.Booking?.User?.Language);

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
                .Include(b => b.User)
                .FirstOrDefaultAsync(b => b.Id == order.BookingId, ct);

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

        await db.SaveChangesAsync(ct);

        var fresh = await LoadOrder(id, ct);
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
                var t = EmailTranslations.For(booking.User?.Language);

                var textBody =
                    $"{t.BookingConfirmGreeting} {booking.ContactName},\n\n" +
                    $"{body}\n\n" +
                    $"{t.BookingStatusViewLink}: {accountUrl}\n\n" +
                    $"Ruumly\ninfo@ruumly.eu";

                await emailSender.SendAsync(booking.ContactEmail, subject, textBody);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to send booking status email to {Email} for booking {Id}",
                    booking.ContactEmail, booking.Id);
                Sentry.SentrySdk.CaptureException(ex);
            }
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private Task<Order?> LoadOrder(Guid id, CancellationToken ct = default) =>
        db.Orders
            .Include(o => o.Supplier)
            .Include(o => o.FulfillmentEvents.OrderBy(e => e.CreatedAt))
            .Include(o => o.Timeline.OrderBy(t => t.CreatedAt))
            .Include(o => o.Booking).ThenInclude(b => b!.User)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    public OrderDto MapToDto(Order o) => MapOrderToDto(o);

    /// <summary>
    /// Strips internal commercial/operational fields a customer must never see:
    /// the platform margin, what the partner is paid, the posting channel, raw
    /// fulfillment events (whose Detail embeds partner endpoints/recipients) and
    /// any timeline Detail (approver names, endpoints). Customer-facing prices
    /// (BasePrice / PlatformPrice / ExtrasTotal / Total) are deliberately kept.
    /// </summary>
    private static OrderDto Redact(OrderDto d) => d with
    {
        SupplierPrice     = 0m,
        Margin            = 0m,
        PostingChannel    = null,
        ApprovalMode      = null,
        ProviderNotes     = null,
        LastContactAt     = null,
        FulfillmentEvents = new List<FulfillmentEventDto>(),
        Timeline          = d.Timeline.Select(t => t with { Detail = null }).ToList(),
    };

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
        ApprovalMode:     o.ApprovalMode.ToString().ToLower(),
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
