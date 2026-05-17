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

public class InvoiceService(RuumlyDbContext db, IHttpContextAccessor http) : IInvoiceService
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

    public async Task<InvoiceDto> GenerateAsync(Guid bookingId)
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

        var invoice = new Invoice
        {
            Id          = Guid.NewGuid(),
            BookingId   = booking.Id,
            Amount      = booking.Total,
            Status      = InvoiceStatus.Pending,
            IssuedAt    = DateTime.UtcNow,
            Description = $"{booking.Listing?.Title ?? "Teenus"} — {booking.Duration}",
            CreatedAt   = DateTime.UtcNow,
        };

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        return MapToDto(invoice);
    }

    public async Task<InvoiceDto> MarkPaidAsync(Guid id)
    {
        var invoice = await db.Invoices.FindAsync(id)
            ?? throw new NotFoundException($"Invoice {id} not found.");

        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

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
