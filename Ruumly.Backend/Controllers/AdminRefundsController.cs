using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

[Route("api/admin/bookings")]
public class AdminRefundsController(
    RuumlyDbContext db,
    IHttpContextAccessor http,
    INotificationService notificationService) : AdminBaseController(db)
{
    private string Lang => http.HttpContext?.Request.GetLang() ?? "et";
    private string Msg(string key) => ErrorMessages.Get(key, Lang);

    // ── POST /api/admin/bookings/{id}/refund ───────────────────────────────────
    /// <summary>
    /// MVP refund: marks the invoice as PendingRefund and records a timeline
    /// entry. Actual bank transfer is handled manually by the admin team.
    /// </summary>
    [HttpPost("{id:guid}/refund")]
    public async Task<IActionResult> Refund(Guid id)
    {
        var booking = await Db.Bookings
            .Include(b => b.Invoice)
            .Include(b => b.User)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (booking is null)
            return NotFound(new { error = Msg("BOOKING_NOT_FOUND") });

        var invoice = booking.Invoice;

        if (invoice is null)
            return BadRequest(new { error = Msg("NO_INVOICE_FOR_BOOKING") });

        if (invoice.Status != InvoiceStatus.Paid)
            return BadRequest(new { error = Msg("REFUND_REQUIRES_PAID_INVOICE") });

        // Mark invoice as pending refund — manual bank transfer follows
        invoice.Status = InvoiceStatus.PendingRefund;

        // Record timeline entry
        Db.BookingTimelines.Add(new BookingTimeline
        {
            Id        = Guid.NewGuid(),
            BookingId = booking.Id,
            Event     = "Refund initiated",
            Status    = booking.Status,
            CreatedAt = DateTime.UtcNow,
        });

        // Queue audit log before saving so entity + log commit atomically.
        Audit(
            action: "booking.refund_initiated",
            actor:  User.Identity?.Name ?? "admin",
            target: booking.Id.ToString(),
            detail: $"Invoice {invoice.Id}, amount €{invoice.Amount:F2}");

        await Db.SaveChangesAsync();

        // Notify the customer in their preferred language.
        var tl         = EmailTranslations.For(booking.User?.Language ?? "et");
        var bookingRef = booking.Id.ToString()[..8].ToUpper();
        await notificationService.CreateAsync(
            userId:     booking.UserId,
            type:       NotificationType.Payment,
            title:      tl.RefundInitiatedTitle,
            desc:       tl.RefundInitiatedDesc.Replace("{bookingRef}", bookingRef),
            actionUrl:  "/account?tab=bookings",
            entityId:   booking.Id.ToString(),
            entityType: "booking");

        return Ok(new
        {
            bookingId = booking.Id,
            invoiceId = invoice.Id,
            amount    = invoice.Amount,
            status    = invoice.Status.ToString().ToLower(),
            message   = "Refund initiated. Invoice marked as pending refund.",
        });
    }

    // ── POST /api/admin/bookings/{id}/mark-refunded ────────────────────────────
    /// <summary>
    /// Confirms the refund was completed (bank transfer done).
    /// Transitions invoice PendingRefund → Refunded and cancels the payout entry.
    /// </summary>
    [HttpPost("{id:guid}/mark-refunded")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> MarkRefunded(Guid id)
    {
        var invoice = await Db.Invoices.FindAsync(id);
        if (invoice is null) return NotFound();

        if (invoice.Status != InvoiceStatus.PendingRefund)
            return BadRequest(Error("Invoice must be in PendingRefund status"));

        invoice.Status = InvoiceStatus.Refunded;
        await Db.SaveChangesAsync();

        // Cancel the payout entry so the supplier is not paid for a refunded order
        var payout = await Db.PayoutEntries
            .FirstOrDefaultAsync(p => p.Order.BookingId == invoice.BookingId
                                   && p.Status == PayoutStatus.Pending);
        if (payout is not null)
        {
            payout.Status = PayoutStatus.Cancelled;
            await Db.SaveChangesAsync();
        }

        return Ok(new { invoice.Id, status = "refunded" });
    }
}
