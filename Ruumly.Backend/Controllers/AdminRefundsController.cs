using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

[Route("api/admin/bookings")]
public class AdminRefundsController(
    RuumlyDbContext db,
    INotificationService notificationService) : AdminBaseController(db)
{
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
            return NotFound(new { error = "Broneeringut ei leitud." });

        var invoice = booking.Invoice;

        if (invoice is null)
            return BadRequest(new { error = "Sellel broneeringul pole arvet." });

        if (invoice.Status != InvoiceStatus.Paid)
            return BadRequest(new { error = $"Tagastus on võimalik ainult tasutud arvetel. Praegune staatus: {invoice.Status}." });

        // Mark invoice as pending refund — manual bank transfer follows
        invoice.Status = InvoiceStatus.PendingRefund;

        // Record timeline entry
        Db.BookingTimelines.Add(new BookingTimeline
        {
            Id        = Guid.NewGuid(),
            BookingId = booking.Id,
            Event     = "Tagastus algatatud",
            Status    = booking.Status,
            CreatedAt = DateTime.UtcNow,
        });

        await Db.SaveChangesAsync();

        // Notify the customer
        await notificationService.CreateAsync(
            userId:     booking.UserId,
            type:       NotificationType.Payment,
            title:      "Tagastus algatatud",
            desc:       $"Teie broneeringu #{booking.Id.ToString()[..8].ToUpper()} tagastus on algatatud. Summa kantakse teie kontole 3–5 tööpäeva jooksul.",
            actionUrl:  $"/account?tab=bookings",
            entityId:   booking.Id.ToString(),
            entityType: "booking");

        await Audit(
            action: "booking.refund_initiated",
            actor:  User.Identity?.Name ?? "admin",
            target: booking.Id.ToString(),
            detail: $"Invoice {invoice.Id}, amount €{invoice.Amount:F2}");

        return Ok(new
        {
            bookingId = booking.Id,
            invoiceId = invoice.Id,
            amount    = invoice.Amount,
            status    = invoice.Status.ToString().ToLower(),
            message   = "Tagastus algatatud. Arve on märgitud ootel tagastuseks.",
        });
    }
}
