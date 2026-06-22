using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Implementations;
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
    public async Task<IActionResult> Refund(Guid id, [FromBody] RefundRequest? body = null)
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

        // Cancel the supplier payout so a refunded booking can never pay out — even if
        // the order is later confirmed (Accrued -> Pending promotion). Mirrors the same
        // guard in MarkRefunded/CancelAndRefund; without it a refund here leaves the
        // payout live and the supplier gets paid for money the customer got back.
        var orderId = await Db.Orders
            .Where(o => o.BookingId == booking.Id)
            .Select(o => o.Id)
            .FirstOrDefaultAsync();
        var payout = await Db.PayoutEntries
            .FirstOrDefaultAsync(p => p.OrderId == orderId
                                   && (p.Status == PayoutStatus.Pending
                                    || p.Status == PayoutStatus.Accrued));
        if (payout is not null)
            payout.Status = PayoutStatus.Cancelled;

        // Support a partial refund: a retained call-out fee, a withheld trailer deposit,
        // a mid-month storage exit. Default to the full invoice amount when none given.
        var refundAmount = body?.RefundAmount is decimal ra && ra > 0m && ra <= invoice.Amount
            ? ra
            : invoice.Amount;
        var isPartial = refundAmount < invoice.Amount;

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
            detail: $"Invoice {invoice.Id}, refund €{refundAmount:F2} of €{invoice.Amount:F2}"
                    + (isPartial ? " (partial)" : "")
                    + (string.IsNullOrWhiteSpace(body?.Reason) ? "" : $" — {body!.Reason!.Trim()}"));

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
            bookingId    = booking.Id,
            invoiceId    = invoice.Id,
            amount       = invoice.Amount,
            refundAmount,
            isPartial,
            status       = invoice.Status.ToString().ToLower(),
            message      = "Refund initiated. Invoice marked as pending refund.",
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

        // Cancel the payout entry so the supplier is not paid for a refunded order.
        // Resolve via the scalar OrderId (no navigation in the predicate) so this is
        // reliably SQL-translatable and works under the InMemory test provider.
        var refundedOrderId = await Db.Orders
            .Where(o => o.BookingId == invoice.BookingId)
            .Select(o => o.Id)
            .FirstOrDefaultAsync();
        var payout = await Db.PayoutEntries
            .FirstOrDefaultAsync(p => p.OrderId == refundedOrderId
                                   && (p.Status == PayoutStatus.Pending
                                    || p.Status == PayoutStatus.Accrued));
        if (payout is not null)
        {
            payout.Status = PayoutStatus.Cancelled;
            await Db.SaveChangesAsync();
        }

        return Ok(new { invoice.Id, status = "refunded" });
    }

    // ── POST /api/admin/invoices/{id}/mark-paid ────────────────────────────────
    /// <summary>
    /// Admin fallback: manually marks an invoice as paid.
    /// Triggers order dispatch (if AutoDispatch) exactly as the Montonio webhook would.
    /// Use this when Montonio is not connected or the webhook was missed.
    /// </summary>
    [HttpPost("/api/admin/invoices/{id:guid}/mark-paid")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AdminMarkInvoicePaid(
        Guid id,
        [FromServices] IInvoiceService invoiceService,
        [FromBody] MarkInvoicePaidRequest? body = null)
    {
        try
        {
            var invoice = await invoiceService.MarkPaidAsync(id, body?.Reference);

            // Capture the operator-supplied bank reference + received amount + note in the
            // audit trail — the reconciliation record for the manual (no-PSP) bank-transfer
            // flow. The reference is also persisted on the invoice (PaymentOrderId).
            var detail = "Invoice manually marked as paid by admin";
            if (!string.IsNullOrWhiteSpace(body?.Reference))
                detail += $". Bank ref: {body!.Reference!.Trim()}";
            if (body?.ReceivedAmount is decimal received)
            {
                var match = Math.Abs(received - invoice.Amount) < 0.01m ? "MATCH" : "MISMATCH";
                detail += $". Received €{received:F2} (expected €{invoice.Amount:F2}) — {match}";
            }
            if (!string.IsNullOrWhiteSpace(body?.Note))
                detail += $". Note: {body!.Note!.Trim()}";

            Audit(
                action: "invoice.manual_mark_paid",
                actor:  User.Identity?.Name ?? "admin",
                target: id.ToString(),
                detail: detail);
            await Db.SaveChangesAsync();

            return Ok(invoice);
        }
        catch (NotFoundException)
        {
            return NotFound(Error("Invoice not found"));
        }
    }

    // ── POST /api/admin/orders/{id}/cancel-and-refund ─────────────────────────
    /// <summary>
    /// Admin operation: cancels a booking + order and transitions the invoice to
    /// the correct refund state. Does NOT call Montonio's refund API yet —
    /// bank refund is manual. Correct state machine:
    ///   Paid invoice     → PendingRefund (admin processes bank transfer manually)
    ///   Unpaid invoice   → Refunded (voided — no money was taken)
    ///   Payout entry     → Cancelled
    ///   Booking / Order  → Cancelled
    /// </summary>
    [HttpPost("/api/admin/orders/{id:guid}/cancel-and-refund")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CancelAndRefund(Guid id)
    {
        var order = await Db.Orders
            .Include(o => o.Booking)
                .ThenInclude(b => b.Invoice)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null)
            return NotFound(Error("Order not found"));

        if (order.Status == OrderStatus.Cancelled)
            return BadRequest(Error("Order is already cancelled"));

        var now     = DateTime.UtcNow;
        var booking = order.Booking;
        var invoice = booking?.Invoice;

        // Cancel the order
        order.Status    = OrderStatus.Cancelled;
        order.UpdatedAt = now;

        // Cancel payout entry (Accrued if unconfirmed, Pending if already promoted)
        var payout = await Db.PayoutEntries
            .FirstOrDefaultAsync(p => p.OrderId == order.Id
                                   && (p.Status == PayoutStatus.Pending
                                    || p.Status == PayoutStatus.Accrued));
        if (payout is not null)
            payout.Status = PayoutStatus.Cancelled;

        // Transition invoice state machine
        if (invoice is not null)
        {
            if (invoice.Status == InvoiceStatus.Paid)
                invoice.Status = InvoiceStatus.PendingRefund;  // money taken — needs bank refund
            else if (invoice.Status != InvoiceStatus.Refunded
                  && invoice.Status != InvoiceStatus.PendingRefund)
                invoice.Status = InvoiceStatus.Refunded;  // not yet paid — void immediately
        }

        // Cancel booking if present
        if (booking is not null && booking.Status != BookingStatus.Cancelled)
        {
            booking.Status    = BookingStatus.Cancelled;
            booking.UpdatedAt = now;

            Db.BookingTimelines.Add(new BookingTimeline
            {
                Id        = Guid.NewGuid(),
                BookingId = booking.Id,
                Event     = "Booking cancelled and refund initiated by admin",
                Status    = BookingStatus.Cancelled,
                CreatedAt = now,
            });
        }

        Audit(
            action: "order.cancel_and_refund",
            actor:  User.Identity?.Name ?? "admin",
            target: order.Id.ToString(),
            detail: $"BookingId={booking?.Id}, InvoiceId={invoice?.Id}, Amount={invoice?.Amount:F2}");

        await Db.SaveChangesAsync();

        return Ok(new
        {
            orderId       = order.Id,
            orderStatus   = order.Status.ToString().ToLower(),
            bookingId     = booking?.Id,
            invoiceId     = invoice?.Id,
            invoiceStatus = invoice?.Status.ToString().ToLower(),
            payoutCancelled = payout is not null,
            message = invoice?.Status == InvoiceStatus.PendingRefund
                ? "Order cancelled. Invoice marked PendingRefund — process bank refund manually."
                : "Order cancelled. Invoice voided (no payment taken).",
        });
    }
}

/// <summary>Optional body for the manual mark-paid action: the bank reference and a
/// note the operator matched the wire transfer against (recorded in the audit log).</summary>
public record MarkInvoicePaidRequest(string? Reference, string? Note, decimal? ReceivedAmount = null);

/// <summary>Optional body for a refund: a partial RefundAmount (defaults to the full
/// invoice when omitted) and an optional reason, both recorded in the audit trail.</summary>
public record RefundRequest(decimal? RefundAmount = null, string? Reason = null);
