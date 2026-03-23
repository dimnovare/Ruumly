using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class MessageService(RuumlyDbContext db) : IMessageService
{
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
            // Notify the supplier's contact user (provider)
            var providerUser = await db.Users
                .FirstOrDefaultAsync(u => u.Email == booking.Supplier.ContactEmail);
            if (providerUser is not null)
            {
                db.Notifications.Add(new Notification
                {
                    Id         = Guid.NewGuid(),
                    UserId     = providerUser.Id,
                    Type       = NotificationType.Booking,
                    Title      = "Uus sõnum broneeringus",
                    Desc       = $"{senderName}: {TruncateText(request.Text, 80)}",
                    Read       = false,
                    ActionUrl  = $"/bookings/{bookingId}",
                    EntityId   = bookingId.ToString(),
                    EntityType = "Message",
                    Channel    = NotificationChannel.InApp,
                    CreatedAt  = DateTime.UtcNow,
                });
            }
        }
        else
        {
            // Notify the customer
            db.Notifications.Add(new Notification
            {
                Id         = Guid.NewGuid(),
                UserId     = booking.UserId,
                Type       = NotificationType.Booking,
                Title      = "Uus sõnum broneeringus",
                Desc       = $"{senderName}: {TruncateText(request.Text, 80)}",
                Read       = false,
                ActionUrl  = "/account?tab=bookings",
                EntityId   = bookingId.ToString(),
                EntityType = "Message",
                Channel    = NotificationChannel.InApp,
                CreatedAt  = DateTime.UtcNow,
            });
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
                throw new ForbiddenException("You do not have access to this booking.");
            return;
        }

        // Provider: verify their email matches the supplier's contact email
        var user = await db.Users.FindAsync(userId);
        var supplier = user is not null
            ? await db.Suppliers.FirstOrDefaultAsync(s => s.ContactEmail == user.Email)
            : null;

        if (supplier is null || booking.SupplierId != supplier.Id)
            throw new ForbiddenException("You do not have access to this booking.");
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
