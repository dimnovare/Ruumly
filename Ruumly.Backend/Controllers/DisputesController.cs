using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

/// <summary>
/// Lets a customer (about their booking) or a partner (about their order) raise a
/// trust-and-safety claim — damage, no-show, or a deposit disagreement — and view
/// the claims they've raised. Admins resolve them via <see cref="AdminDisputesController"/>.
/// </summary>
[ApiController]
[Route("api/disputes")]
[Authorize(Roles = "Customer,Provider,Admin")]
public class DisputesController(
    RuumlyDbContext db,
    INotificationService notificationService,
    IBackgroundEmailQueue emailQueue) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Raise([FromBody] RaiseDisputeRequest body)
    {
        if (body.BookingId is null && body.OrderId is null)
            return BadRequest(new { error = "A booking or order reference is required." });
        if (string.IsNullOrWhiteSpace(body.Subject))
            return BadRequest(new { error = "Subject is required." });
        if (body.Subject.Length > 150)
            return BadRequest(new { error = "Subject must be 150 characters or fewer." });
        if (body.Description is { Length: > 4000 })
            return BadRequest(new { error = "Description must be 4000 characters or fewer." });
        if (!Enum.TryParse<DisputeType>(body.Type, ignoreCase: true, out var type))
            return BadRequest(new { error = $"Invalid dispute type '{body.Type}'." });

        // Resolve the booking (the source of truth for ownership + supplier).
        Booking? booking = null;
        if (body.BookingId is Guid bid)
            booking = await db.Bookings.Include(b => b.Order).FirstOrDefaultAsync(b => b.Id == bid);
        else if (body.OrderId is Guid oid)
        {
            var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == oid);
            if (order is not null)
                booking = await db.Bookings.Include(b => b.Order).FirstOrDefaultAsync(b => b.Id == order.BookingId);
        }
        if (booking is null)
            return NotFound(new { error = "Booking or order not found." });

        var userId = User.GetUserId();
        var role   = User.GetUserRole();

        // Ownership: a customer may only dispute their own booking; a provider only
        // their own supplier's. Admin may raise on behalf of either.
        if (role == UserRole.Customer && booking.UserId != userId)
            return StatusCode(403, new { error = "You can only dispute your own booking." });
        if (role == UserRole.Provider)
        {
            var user = await db.Users.FindAsync(userId);
            if (user?.SupplierId is null || user.SupplierId != booking.SupplierId)
                return StatusCode(403, new { error = "You can only dispute your own supplier's orders." });
        }

        var evidence = body.Evidence?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? [];

        var dispute = new Dispute
        {
            Id             = Guid.NewGuid(),
            BookingId      = booking.Id,
            OrderId        = booking.Order?.Id ?? body.OrderId,
            SupplierId     = booking.SupplierId,
            RaisedByUserId = userId,
            RaisedByRole   = role,
            Type           = type,
            Status         = DisputeStatus.Open,
            Subject        = body.Subject.Trim(),
            Description    = (body.Description ?? "").Trim(),
            ContactEmail   = booking.ContactEmail ?? User.GetUserEmail(),
            AmountClaimed  = body.AmountClaimed is decimal a && a >= 0 ? decimal.Round(a, 2) : null,
            EvidenceJson   = JsonSerializer.Serialize(evidence),
            CreatedAt      = DateTime.UtcNow,
            UpdatedAt      = DateTime.UtcNow,
        };
        db.Disputes.Add(dispute);
        await db.SaveChangesAsync();

        // Notify admins in-app + by email so the claim enters the resolution queue.
        var adminIds = await db.Users
            .Where(u => u.Role == UserRole.Admin)
            .Select(u => u.Id)
            .ToListAsync();
        foreach (var aid in adminIds)
            await notificationService.CreateAsync(
                aid, NotificationType.Alert,
                title:      "New dispute raised",
                desc:       $"{type}: {dispute.Subject}",
                actionUrl:  "/admin?tab=disputes",
                entityId:   dispute.Id.ToString(),
                entityType: "dispute");

        emailQueue.EnqueueEmail(
            to:       await OpsInbox.ResolveAsync(db),
            subject:  $"New dispute ({type}) — {dispute.Subject}",
            textBody: $"From: {dispute.ContactEmail} ({role})\nBooking: {dispute.BookingId}\n"
                    + $"Amount claimed: {dispute.AmountClaimed?.ToString() ?? "—"}\n\n{dispute.Description}");

        return Ok(Map(dispute));
    }

    [HttpGet("mine")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine([FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        page  = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);

        var userId = User.GetUserId();
        var role   = User.GetUserRole();

        IQueryable<Dispute> query = db.Disputes;
        if (role == UserRole.Provider)
        {
            var user = await db.Users.FindAsync(userId);
            query = query.Where(d => d.SupplierId == user!.SupplierId);
        }
        else if (role == UserRole.Customer)
        {
            query = query.Where(d => d.RaisedByUserId == userId);
        }
        // Admin: all — but the admin queue endpoint is the richer view.

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

        return Ok(new { items = rows.Select(Map), total, page, limit });
    }

    private static object Map(Dispute d) => new
    {
        d.Id,
        d.BookingId,
        d.OrderId,
        type         = d.Type.ToString().ToLower(),
        status       = d.Status.ToString().ToLower(),
        d.Subject,
        d.Description,
        d.AmountClaimed,
        d.Resolution,
        d.CreatedAt,
        d.ResolvedAt,
    };
}

public record RaiseDisputeRequest(
    Guid? BookingId,
    Guid? OrderId,
    string Type,
    string Subject,
    string? Description = null,
    decimal? AmountClaimed = null,
    List<string>? Evidence = null);
