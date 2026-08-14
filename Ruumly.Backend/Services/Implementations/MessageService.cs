using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class MessageService(
    RuumlyDbContext db,
    INotificationService notificationService,
    IHttpContextAccessor http) : IMessageService
{
    private string Lang => http.HttpContext?.Request.GetLang() ?? "et";
    private string Msg(string key) => ErrorMessages.Get(key, Lang);

    public async Task<List<MessageDto>> GetByBookingIdAsync(Guid bookingId, Guid userId, UserRole role)
    {
        await VerifyAccessAsync(bookingId, userId, role);

        var messages = await db.Messages
            .Where(m => m.BookingId == bookingId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        return messages.Select(MapToDto).ToList();
    }

    public async Task<MessageDto> SendAsync(Guid bookingId, SendMessageRequest request, Guid userId, UserRole role)
    {
        var booking = await db.Bookings
            .Include(b => b.Supplier)
            .Include(b => b.Listing)
            .FirstOrDefaultAsync(b => b.Id == bookingId)
            ?? throw new NotFoundException($"Booking {bookingId} not found.");

        await VerifyAccessAsync(booking, userId, role);

        var senderEnum = role switch
        {
            UserRole.Provider => MessageSender.Provider,
            UserRole.Admin    => MessageSender.Admin,
            _                 => MessageSender.Customer,
        };

        var user = await db.Users.FindAsync(userId);
        var senderName = user?.Name ?? "Unknown";

        var message = new Message
        {
            Id         = Guid.NewGuid(),
            BookingId  = bookingId,
            UserId     = userId,
            From       = senderEnum,
            SenderName = senderName,
            Text       = request.Text,
            Read       = false,
            CreatedAt  = DateTime.UtcNow,
        };
        db.Messages.Add(message);

        // Notify the other party
        if (senderEnum == MessageSender.Customer)
        {
            // Prefer SupplierId-based lookup; fall back to contact email. The two
            // columns are filled by different hands — the login is stored canonical,
            // the supplier row keeps its imported casing — so the fallback compares
            // case-insensitively or it silently notifies nobody.
            var supplierEmail = EmailValidation.Normalize(booking.Supplier.ContactEmail);
            var providerUser = await db.Users
                .FirstOrDefaultAsync(u => u.SupplierId == booking.SupplierId && u.Role == UserRole.Provider)
                ?? await db.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == supplierEmail);

            if (providerUser is not null)
            {
                var tProv = EmailTranslations.For(providerUser.Language);
                await notificationService.CreateAsync(
                    providerUser.Id,
                    NotificationType.Booking,
                    tProv.NotifNewMessage,
                    $"{senderName}: {TruncateText(request.Text, 80)}",
                    actionUrl:  "/provider?ptab=orders",
                    entityId:   bookingId.ToString(),
                    entityType: "Message");
            }
        }
        else
        {
            var bookingUser = await db.Users.FindAsync(booking.UserId);
            var tCust = EmailTranslations.For(bookingUser?.Language);
            await notificationService.CreateAsync(
                booking.UserId,
                NotificationType.Booking,
                tCust.NotifNewMessage,
                $"{senderName}: {TruncateText(request.Text, 80)}",
                actionUrl:  $"/account?tab=messages&bookingId={bookingId}",
                entityId:   bookingId.ToString(),
                entityType: "Message");
        }

        await db.SaveChangesAsync();
        return MapToDto(message);
    }

    public async Task MarkReadAsync(Guid bookingId, Guid userId, UserRole role)
    {
        await VerifyAccessAsync(bookingId, userId, role);

        // Mark messages from the OTHER party as read
        var callerSender = role switch
        {
            UserRole.Provider => MessageSender.Provider,
            UserRole.Admin    => MessageSender.Admin,
            _                 => MessageSender.Customer,
        };

        var unread = await db.Messages
            .Where(m => m.BookingId == bookingId && !m.Read && m.From != callerSender)
            .ToListAsync();

        foreach (var msg in unread)
            msg.Read = true;

        await db.SaveChangesAsync();
    }

    public async Task<int> GetUnreadCountAsync(Guid userId, UserRole role)
    {
        var callerSender = role switch
        {
            UserRole.Provider => MessageSender.Provider,
            UserRole.Admin    => MessageSender.Admin,
            _                 => MessageSender.Customer,
        };

        if (role == UserRole.Customer)
        {
            var bookingIds = await db.Bookings
                .Where(b => b.UserId == userId)
                .Select(b => b.Id)
                .ToListAsync();

            return await db.Messages
                .CountAsync(m => bookingIds.Contains(m.BookingId)
                              && !m.Read
                              && m.From != callerSender);
        }

        // Provider/Admin: messages on their supplier's bookings
        var user = await db.Users.FindAsync(userId);
        if (user?.SupplierId is null) return 0;

        var supplierBookingIds = await db.Bookings
            .Where(b => b.SupplierId == user.SupplierId)
            .Select(b => b.Id)
            .ToListAsync();

        return await db.Messages
            .CountAsync(m => supplierBookingIds.Contains(m.BookingId)
                          && !m.Read
                          && m.From != callerSender);
    }

    // ─── Access guard ─────────────────────────────────────────────────────────

    private async Task VerifyAccessAsync(Guid bookingId, Guid userId, UserRole role)
    {
        var booking = await db.Bookings
            .Include(b => b.Supplier)
            .FirstOrDefaultAsync(b => b.Id == bookingId)
            ?? throw new NotFoundException($"Booking {bookingId} not found.");

        await VerifyAccessAsync(booking, userId, role);
    }

    private async Task VerifyAccessAsync(Booking booking, Guid userId, UserRole role)
    {
        if (role == UserRole.Admin) return;

        if (role == UserRole.Customer)
        {
            if (booking.UserId != userId)
                throw new ForbiddenException(Msg("BOOKING_NOT_FOUND"));
            return;
        }

        // Provider: verify via SupplierId FK, falling back to email match for legacy
        // users. Case-insensitive: one mailbox is one identity however it was typed,
        // and a provider whose supplier row was imported "Info@Yard.ee" must not lose
        // their own bookings the moment their login is stored canonically.
        //
        // The blank-address check is load-bearing: imported directory rows store
        // ContactEmail as "" when the source had none, so an empty address on this
        // side would match them all and hand out someone else's booking.
        var user = await db.Users.FindAsync(userId);
        var userEmail = EmailValidation.Normalize(user?.Email);
        var supplier = user?.SupplierId.HasValue == true
            ? await db.Suppliers.FindAsync(user.SupplierId.Value)
            : userEmail.Length > 0
                ? await db.Suppliers.FirstOrDefaultAsync(s => s.ContactEmail.ToLower() == userEmail)
                : null;

        if (supplier is null || booking.SupplierId != supplier.Id)
            throw new ForbiddenException(Msg("BOOKING_NOT_FOUND"));
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static MessageDto MapToDto(Message m) => new(
        Id:         m.Id,
        BookingId:  m.BookingId,
        From:       m.From.ToString().ToLower(),
        SenderName: m.SenderName,
        Text:       m.Text,
        Read:       m.Read,
        CreatedAt:  m.CreatedAt.ToString("yyyy-MM-dd HH:mm")
    );

    private static string TruncateText(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";
}
