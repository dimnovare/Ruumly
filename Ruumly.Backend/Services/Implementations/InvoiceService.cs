using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class InvoiceService(RuumlyDbContext db) : IInvoiceService
{
    public async Task<List<InvoiceDto>> GetAllAsync(Guid userId, UserRole role)
    {
        var query = db.Invoices
            .Include(i => i.Booking)
            .AsQueryable();

        if (role == UserRole.Customer)
        {
            query = query.Where(i => i.Booking.UserId == userId);
        }
        else if (role == UserRole.Provider)
        {
            var user = await db.Users.FindAsync(userId);
            if (user is not null)
            {
                var supplier = await db.Suppliers
                    .FirstOrDefaultAsync(s => s.ContactEmail == user.Email);
                if (supplier is not null)
                    query = query.Where(i => i.Booking.SupplierId == supplier.Id);
                else
                    return [];
            }
        }
        // Admin: no filter

        var invoices = await query
            .OrderByDescending(i => i.IssuedAt)
            .ToListAsync();

        return invoices.Select(MapToDto).ToList();
    }

    public async Task<InvoiceDto?> GetByBookingIdAsync(Guid bookingId, Guid userId, UserRole role)
    {
        var invoice = await db.Invoices
            .Include(i => i.Booking)
            .FirstOrDefaultAsync(i => i.BookingId == bookingId);

        if (invoice is null) return null;

        if (role == UserRole.Customer && invoice.Booking.UserId != userId)
            throw new ForbiddenException("You do not have access to this invoice.");

        if (role == UserRole.Provider)
        {
            var user     = await db.Users.FindAsync(userId);
            var supplier = user is not null
                ? await db.Suppliers.FirstOrDefaultAsync(s => s.ContactEmail == user.Email)
                : null;
            if (supplier is null || invoice.Booking.SupplierId != supplier.Id)
                throw new ForbiddenException("You do not have access to this invoice.");
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
