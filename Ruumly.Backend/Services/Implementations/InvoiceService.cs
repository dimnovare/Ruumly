using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class InvoiceService(
    RuumlyDbContext db,
    IHttpContextAccessor http,
    ILogger<InvoiceService> logger) : IInvoiceService
{
    private string Lang => http.HttpContext?.Request.GetLang() ?? "et";
    private string Msg(string key) => ErrorMessages.Get(key, Lang);

    public async Task<PaginatedResult<InvoiceDto>> GetAllAsync(Guid userId, UserRole role, int page = 1, int limit = 100)
    {
        page  = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 200);

        var query = db.Invoices
            .Include(i => i.Booking)
            .AsQueryable();

        if (role == UserRole.Customer)
        {
            query = query.Where(i => i.Booking.UserId == userId);
        }
        else if (role == UserRole.Provider)
        {
            var supplierId = await db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.SupplierId)
                .FirstOrDefaultAsync();
            if (supplierId is null) return new PaginatedResult<InvoiceDto>([], 0, page, limit, false);
            query = query.Where(i => i.Booking.SupplierId == supplierId.Value);
        }
        // Admin: no filter

        var total    = await query.CountAsync();
        var invoices = await query
            .OrderByDescending(i => i.IssuedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

        var data = invoices.Select(MapToDto).ToList();
        return new PaginatedResult<InvoiceDto>(data, total, page, limit,
            (page - 1) * limit + data.Count < total);
    }

    public async Task<InvoiceDto?> GetByBookingIdAsync(Guid bookingId, Guid userId, UserRole role)
    {
        var invoice = await db.Invoices
            .Include(i => i.Booking)
            .FirstOrDefaultAsync(i => i.BookingId == bookingId);

        if (invoice is null) return null;

        if (role == UserRole.Customer && invoice.Booking.UserId != userId)
            throw new ForbiddenException(Msg("BOOKING_NOT_FOUND"));

        if (role == UserRole.Provider)
        {
            var supplierId = await db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.SupplierId)
                .FirstOrDefaultAsync();
            if (supplierId is null || invoice.Booking.SupplierId != supplierId.Value)
                throw new ForbiddenException(Msg("BOOKING_NOT_FOUND"));
        }

        return MapToDto(invoice);
    }

    public async Task<InvoiceDto> GenerateAsync(Guid bookingId, string? paymentMethod = null)
    {
        // Idempotent: return existing invoice if already generated
        var existing = await db.Invoices
            .FirstOrDefaultAsync(i => i.BookingId == bookingId);
        if (existing is not null)
            return MapToDto(existing);

        var booking = await db.Bookings
            .Include(b => b.Listing)
            .FirstOrDefaultAsync(b => b.Id == bookingId)
            ?? throw new NotFoundException($"Booking {bookingId} not found.");

        // A payable invoice must have a positive amount. A €0 total means a pricing
        // bug upstream; without this guard the Montonio webhook's amount check passes
        // for 0 == 0, marking the invoice Paid and dispatching the order to the
        // supplier for a zero-revenue booking. Fail loudly instead.
        if (booking.Total <= 0m)
            throw new ArgumentException(
                $"Cannot generate an invoice for booking {bookingId}: total is {booking.Total}. " +
                "A payable invoice requires a positive amount.");

        var invoice = new Invoice
        {
            Id          = Guid.NewGuid(),
            BookingId   = booking.Id,
            Amount      = booking.Total,
            Status      = InvoiceStatus.Pending,
            PaymentMethod = string.IsNullOrWhiteSpace(paymentMethod)
                ? null
                : paymentMethod.Trim().ToLowerInvariant(),
            IssuedAt    = DateTime.UtcNow,
            Description = $"{booking.Listing?.Title ?? "Teenus"} — {booking.Duration}",
            CreatedAt   = DateTime.UtcNow,
        };

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        return MapToDto(invoice);
    }

    public async Task<InvoiceDto> MarkPaidAsync(Guid id, string? paymentReference = null)
    {
        var invoice = await db.Invoices.FindAsync(id)
            ?? throw new NotFoundException($"Invoice {id} not found.");

        // Idempotent — if already paid, return without side effects.
        if (invoice.Status == InvoiceStatus.Paid)
            return MapToDto(invoice);

        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidAt = DateTime.UtcNow;
        // Persist the operator-supplied bank reference so the manual (no-PSP) wire can be
        // reconciled to this invoice later — not just left in the audit log.
        if (!string.IsNullOrWhiteSpace(paymentReference))
            invoice.PaymentOrderId = paymentReference.Trim();
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Invoice {InvoiceId} manually marked as paid. BookingId={BookingId} Amount={Amount}",
            invoice.Id, invoice.BookingId, invoice.Amount);

        // Trigger order dispatch — same logic as the Montonio webhook confirms payment.
        // This ensures the admin mark-paid path is functionally equivalent to a webhook.
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
                "Invoice {InvoiceId} manually marked paid — dispatching order {OrderId}",
                invoice.Id, order.Id);
        }
        else if (order is not null && !order.AutoDispatch
                 && order.Status == OrderStatus.Created)
        {
            // Needs manual approval — log only; admins handle dispatch
            logger.LogInformation(
                "Invoice {InvoiceId} manually marked paid — order {OrderId} awaits admin approval",
                invoice.Id, order.Id);
        }
        else if (order is null)
        {
            logger.LogWarning(
                "Invoice {InvoiceId} manually marked paid but no Order found for booking {BookingId}",
                invoice.Id, invoice.BookingId);
        }

        return MapToDto(invoice);
    }

    // ─── Mapping ──────────────────────────────────────────────────────────────

    private static InvoiceDto MapToDto(Invoice i) => new(
        Id:          i.Id,
        BookingId:   i.BookingId,
        Amount:      i.Amount,
        Status:      i.Status.ToString().ToLower(),
        IssuedAt:    i.IssuedAt.ToString("yyyy-MM-dd HH:mm"),
        PaidAt:      i.PaidAt?.ToString("yyyy-MM-dd HH:mm"),
        Description: i.Description
    );
}
