using Hangfire;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

/// <summary>
/// Background jobs for order dispatch and confirmation emails.
/// Methods are enqueued via Hangfire so booking creation returns immediately,
/// regardless of supplier API latency or email delivery time.
/// </summary>
public class BackgroundOrderDispatchService(
    RuumlyDbContext db,
    IIntegrationDispatchService dispatchService,
    IEmailSender emailSender,
    IConfiguration config,
    ILogger<BackgroundOrderDispatchService> logger)
{
    /// <summary>
    /// Dispatches an order to the supplier's integration endpoint.
    /// Retried up to 3 times with exponential backoff on failure.
    /// </summary>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 300, 600])]
    public async Task DispatchOrderAsync(Guid orderId)
    {
        var order = await db.Orders
            .Include(o => o.Supplier)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order is null)
        {
            logger.LogWarning(
                "BackgroundDispatch: order {OrderId} not found — skipping",
                orderId);
            return;
        }

        try
        {
            await dispatchService.DispatchAsync(order, order.Supplier);
        }
        catch (Exception ex)
        {
            // Schedule admin notification after retries are exhausted (~16 min total backoff)
            BackgroundJob.Schedule<BackgroundOrderDispatchService>(
                x => x.NotifyDispatchFailure(orderId, ex.Message),
                TimeSpan.FromMinutes(20));
            throw; // Re-throw so Hangfire records the failure and retries
        }
    }

    /// <summary>
    /// Notifies admins when an order dispatch permanently fails after all retries.
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task NotifyDispatchFailure(Guid orderId, string errorMessage)
    {
        var order = await db.Orders
            .Include(o => o.Supplier)
            .FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null) return;

        var admins = await db.Users
            .Where(u => u.Role == UserRole.Admin)
            .ToListAsync();

        foreach (var admin in admins)
        {
            await db.Notifications.AddAsync(new Notification
            {
                Id        = Guid.NewGuid(),
                UserId    = admin.Id,
                Type      = NotificationType.Order,
                Title     = $"Dispatch failed: {order.Supplier?.Name}",
                Message   = $"Order {orderId} failed after 3 retries. Error: {errorMessage}",
                ActionUrl = "/admin?tab=orders",
                CreatedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();

        logger.LogError("Order {OrderId} dispatch permanently failed after retries: {Error}",
            orderId, errorMessage);
    }

    /// <summary>
    /// Sends the booking confirmation email to the customer.
    /// Retried up to 3 times on failure.
    /// </summary>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 300, 600])]
    public async Task SendBookingConfirmationEmailAsync(Guid bookingId)
    {
        var booking = await db.Bookings
            .Include(b => b.Listing)
            .FirstOrDefaultAsync(b => b.Id == bookingId);

        if (booking is null || string.IsNullOrWhiteSpace(booking.ContactEmail))
            return;

        var bookingUser = await db.Users.FindAsync(booking.UserId);
        var lang        = bookingUser?.Language ?? "et";
        var t           = EmailTranslations.For(lang);
        var accountUrl  = $"{config["AppUrl"]}/account?tab=bookings";

        var textBody =
            $"{t.BookingConfirmGreeting} {booking.ContactName},\n\n" +
            $"{t.BookingConfirmBody}\n\n" +
            $"{t.BookingConfirmService}: {booking.Listing?.Title}\n" +
            $"{t.BookingConfirmStartDate}: {booking.StartDate:dd.MM.yyyy}\n" +
            $"{t.BookingConfirmPeriod}: {booking.Duration}\n" +
            $"{t.BookingConfirmTotal}: €{booking.Total:F2}" +
            (booking.VatAmount > 0
                ? $" ({t.BookingConfirmVat} €{booking.VatAmount:F2})"
                : "") +
            $"\n\n{t.BookingConfirmNext}\n{accountUrl}\n\n" +
            $"Ruumly\ninfo@ruumly.eu";

        var subject =
            $"{t.BookingConfirmSubject} #{booking.Id.ToString()[..8].ToUpper()}";

        await emailSender.SendAsync(booking.ContactEmail, subject, textBody);

        logger.LogInformation(
            "Booking confirmation email sent to {Email} for booking {Id}",
            booking.ContactEmail, booking.Id);
    }
}
