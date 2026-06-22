using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
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

        await Db.SaveChangesAsync();

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
        });
    }
}

public record UpdateDisputeRequest(string? Status, string? AdminNotes, string? Resolution);
