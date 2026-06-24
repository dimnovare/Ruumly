using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

/// <summary>Admin resolution queue for trust-and-safety disputes.</summary>
[Route("api/admin/disputes")]
public class AdminDisputesController(
    RuumlyDbContext db,
    INotificationService notificationService,
    ILogger<AdminDisputesController> logger) : AdminBaseController(db)
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? status,
        [FromQuery] int page  = 1,
        [FromQuery] int limit = 50)
    {
        page  = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);

        var query = Db.Disputes.AsQueryable();
        if (!string.IsNullOrEmpty(status) &&
            Enum.TryParse<DisputeStatus>(status, ignoreCase: true, out var parsed))
            query = query.Where(d => d.Status == parsed);

        var total = await query.CountAsync();
        var rows = await query
            .OrderBy(d => d.Status == DisputeStatus.Open ? 0 : d.Status == DisputeStatus.InReview ? 1 : 2)
            .ThenByDescending(d => d.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

        // Enrich with partner + listing context in two batch queries (no N+1).
        var supplierIds = rows.Select(r => r.SupplierId).Distinct().ToList();
        var bookingIds  = rows.Where(r => r.BookingId.HasValue).Select(r => r.BookingId!.Value).Distinct().ToList();

        var supplierNames = await Db.Suppliers
            .Where(s => supplierIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name);
        var listingTitles = await Db.Bookings
            .Where(b => bookingIds.Contains(b.Id))
            .Select(b => new { b.Id, Title = b.Listing!.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title);

        var items = rows.Select(d => new
        {
            d.Id,
            d.BookingId,
            d.OrderId,
            d.SupplierId,
            supplierName  = supplierNames.GetValueOrDefault(d.SupplierId),
            listingTitle  = d.BookingId is Guid b ? listingTitles.GetValueOrDefault(b) : null,
            type          = d.Type.ToString().ToLower(),
            status        = d.Status.ToString().ToLower(),
            raisedByRole  = d.RaisedByRole.ToString().ToLower(),
            d.Subject,
            d.Description,
            d.ContactEmail,
            d.AmountClaimed,
            evidence      = DeserializeEvidence(d.EvidenceJson),
            d.AdminNotes,
            d.Resolution,
            d.CreatedAt,
            d.ResolvedAt,
        });

        return Ok(new { total, page, limit, items });
    }

    private List<string> DeserializeEvidence(string json)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch
        {
            logger.LogWarning("Failed to deserialize dispute evidence (len={Len})", json?.Length ?? 0);
            return [];
        }
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDisputeRequest body)
    {
        var dispute = await Db.Disputes.FirstOrDefaultAsync(d => d.Id == id);
        if (dispute is null) return NotFound(Error("Dispute not found."));

        if (body.AdminNotes is { Length: > 4000 } || body.Resolution is { Length: > 2000 })
            return BadRequest(Error("Notes or resolution too long."));

        // Admin-confirmed refund: validate eligibility up-front so resolve + refund are
        // atomic (either both apply or the request is rejected before anything changes).
        var issueRefund = body.IssueRefund == true;
        Booking? refundBooking = null;
        Invoice? refundInvoice = null;
        Guid refundOrderId = default;
        if (issueRefund)
        {
            if (dispute.BookingId is not Guid bid)
                return BadRequest(Error("This dispute is not linked to a booking, so it cannot be refunded."));
            refundBooking = await Db.Bookings.Include(b => b.User).FirstOrDefaultAsync(b => b.Id == bid);
            if (refundBooking is null)
                return BadRequest(Error("The disputed booking no longer exists."));
            refundInvoice = await Db.Invoices.FirstOrDefaultAsync(i => i.BookingId == bid);
            if (refundInvoice is null || refundInvoice.Status != InvoiceStatus.Paid)
                return BadRequest(Error("Cannot issue a refund: the booking has no paid invoice."));
            refundOrderId = await Db.Orders.Where(o => o.BookingId == bid).Select(o => o.Id).FirstOrDefaultAsync();
        }

        var becameTerminal = false;
        if (!string.IsNullOrEmpty(body.Status) &&
            Enum.TryParse<DisputeStatus>(body.Status, ignoreCase: true, out var newStatus))
        {
            var wasTerminal = dispute.Status is DisputeStatus.Resolved or DisputeStatus.Rejected;
            dispute.Status = newStatus;
            var isTerminal = newStatus is DisputeStatus.Resolved or DisputeStatus.Rejected;
            if (isTerminal && !wasTerminal)
            {
                dispute.ResolvedAt = DateTime.UtcNow;
                becameTerminal = true;
            }
            else if (!isTerminal)
            {
                dispute.ResolvedAt = null;
            }
        }

        if (body.AdminNotes is not null) dispute.AdminNotes = body.AdminNotes;
        if (body.Resolution is not null) dispute.Resolution = body.Resolution;
        dispute.UpdatedAt = DateTime.UtcNow;

        Audit("dispute.updated", User.Identity?.Name ?? "admin", dispute.Id.ToString(),
              $"Status: {dispute.Status}");

        // Execute the refund in the same transaction: invoice -> PendingRefund (manual
        // bank transfer follows) and cancel the supplier payout (Pending or Accrued) so
        // the partner is never paid for a refunded booking. Mirrors AdminRefundsController.
        var refundIssued = false;
        if (issueRefund && refundInvoice is not null && refundBooking is not null)
        {
            refundInvoice.Status = InvoiceStatus.PendingRefund;
            var payout = await Db.PayoutEntries.FirstOrDefaultAsync(p => p.OrderId == refundOrderId
                && (p.Status == PayoutStatus.Pending || p.Status == PayoutStatus.Accrued));
            if (payout is not null) payout.Status = PayoutStatus.Cancelled;

            Db.BookingTimelines.Add(new BookingTimeline
            {
                Id = Guid.NewGuid(), BookingId = refundBooking.Id,
                Event = "Refund initiated (dispute resolved)",
                Status = refundBooking.Status, CreatedAt = DateTime.UtcNow,
            });
            var amount = dispute.AmountClaimed is decimal a && a > 0m && a <= refundInvoice.Amount
                ? a : refundInvoice.Amount;
            Audit("dispute.refund_issued", User.Identity?.Name ?? "admin", dispute.Id.ToString(),
                  $"Booking {refundBooking.Id}, invoice {refundInvoice.Id} -> PendingRefund, amount €{amount:F2}");
            refundIssued = true;
        }

        await Db.SaveChangesAsync();

        // Notify the customer of the refund (in their language).
        if (refundIssued && refundBooking is not null)
        {
            var tl = EmailTranslations.For(refundBooking.User?.Language ?? "et");
            var bookingRef = refundBooking.Id.ToString()[..8].ToUpper();
            await notificationService.CreateAsync(
                refundBooking.UserId, NotificationType.Payment,
                title:      tl.RefundInitiatedTitle,
                desc:       tl.RefundInitiatedDesc.Replace("{bookingRef}", bookingRef),
                actionUrl:  "/account?tab=bookings",
                entityId:   refundBooking.Id.ToString(),
                entityType: "booking");
        }

        // Tell the raiser their claim has been resolved/rejected (in-app).
        if (becameTerminal && dispute.RaisedByUserId is Guid raiser)
            await notificationService.CreateAsync(
                raiser, NotificationType.Alert,
                title:      dispute.Status == DisputeStatus.Resolved ? "Dispute resolved" : "Dispute closed",
                desc:       dispute.Resolution ?? dispute.Subject,
                actionUrl:  "/account",
                entityId:   dispute.Id.ToString(),
                entityType: "dispute");

        return Ok(new
        {
            dispute.Id,
            status     = dispute.Status.ToString().ToLower(),
            dispute.AdminNotes,
            dispute.Resolution,
            dispute.ResolvedAt,
            refundIssued,
        });
    }
}

// IssueRefund: admin-confirmed dispute → refund. When true (and status is being set
// to resolved), the linked booking's paid invoice is marked PendingRefund and the
// supplier payout is cancelled, in the same transaction as the resolution.
public record UpdateDisputeRequest(string? Status, string? AdminNotes, string? Resolution, bool? IssueRefund = null);
