using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController(IOrderService orderService, RuumlyDbContext db) : ControllerBase
{
    /// <summary>
    /// Returns orders. Admin sees all; Provider sees their supplier's orders; Customer sees their own.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin,Provider,Customer")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int   page       = 1,
        [FromQuery] int   limit      = 50,
        [FromQuery] Guid? supplierId = null)
    {
        var result = await orderService.GetAllAsync(User.GetUserId(), User.GetUserRole(), page, limit, supplierId, HttpContext.RequestAborted);
        return Ok(result);
    }

    /// <summary>
    /// Returns a single order by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Provider")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var order = await orderService.GetByIdAsync(id, HttpContext.RequestAborted);
        if (order is null) return NotFound(new { error = "Not Found", message = "Order not found", statusCode = 404 });
        return Ok(order);
    }

    /// <summary>
    /// Returns the order linked to a specific booking.
    /// </summary>
    [HttpGet("by-booking/{bookingId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByBookingId(Guid bookingId)
    {
        var order = await orderService.GetByBookingIdAsync(bookingId, User.GetUserId(), User.GetUserRole(), HttpContext.RequestAborted);
        if (order is null) return NotFound(new { error = "Not Found", message = "Order not found for this booking", statusCode = 404 });
        return Ok(order);
    }

    /// <summary>
    /// Approves an order and dispatches it to the supplier.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "Admin,Provider")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Approve(Guid id)
    {
        var order = await orderService.ApproveAsync(id, User.GetUserId(), HttpContext.RequestAborted);
        return Ok(order);
    }

    /// <summary>
    /// Rejects an order with a reason.
    /// </summary>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "Admin,Provider")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectOrderRequest body)
    {
        var order = await orderService.RejectAsync(id, body.Reason, User.GetUserId(), HttpContext.RequestAborted);
        return Ok(order);
    }

    /// <summary>
    /// Confirms an order (supplier webhook or admin manual confirmation).
    /// </summary>
    [HttpPost("{id:guid}/confirm")]
    [Authorize(Roles = "Admin,Provider")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Confirm(Guid id)
    {
        var order = await orderService.ConfirmAsync(id, User.GetUserId(), HttpContext.RequestAborted);
        return Ok(order);
    }

    /// <summary>
    /// Admin override for any order status.
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateOrderStatusRequest request)
    {
        var order = await orderService.UpdateStatusAsync(id, request, HttpContext.RequestAborted);
        return Ok(order);
    }
    /// <summary>
    /// Patch lead/CRM fields on an order. Provider (own supplier) or admin only.
    /// </summary>
    [HttpPatch("{id:guid}/lead")]
    [Authorize(Roles = "Admin,Provider")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateLead(Guid id, [FromBody] UpdateOrderLeadRequest body)
    {
        var order = await db.Orders
            .Include(o => o.Supplier)
            .Include(o => o.FulfillmentEvents.OrderBy(e => e.CreatedAt))
            .Include(o => o.Timeline.OrderBy(t => t.CreatedAt))
            .Include(o => o.Booking)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null)
            return NotFound(new { error = "Not Found", message = "Order not found", statusCode = 404 });

        // Authorization: admin always OK; provider must own the supplier
        if (User.GetUserRole() == UserRole.Provider)
        {
            var user = await db.Users.FindAsync(User.GetUserId());
            if (user?.SupplierId != order.SupplierId)
                return StatusCode(403, new { error = "Forbidden", message = "You can only update leads for your own supplier's orders", statusCode = 403 });
        }

        // Validate providerNotes length
        if (body.ProviderNotes is not null && body.ProviderNotes.Length > 2000)
            return BadRequest(new { error = "Bad Request", message = "ProviderNotes must be 2000 characters or fewer", statusCode = 400 });

        // Patch semantics: null means don't change
        if (body.LeadStatus is not null)
        {
            if (!Enum.TryParse<LeadStatus>(body.LeadStatus, ignoreCase: true, out var status))
                return BadRequest(new { error = "Bad Request", message = $"Invalid LeadStatus '{body.LeadStatus}'", statusCode = 400 });
            order.LeadStatus = status;
        }

        if (body.LastContactAt.HasValue)
            order.LastContactAt = body.LastContactAt.Value;

        if (body.ProviderNotes is not null)
            order.ProviderNotes = body.ProviderNotes;

        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(orderService.MapToDto(order));
    }

    /// <summary>
    /// Lead summary for the calling provider's supplier (rolling 30-day window).
    /// </summary>
    [HttpGet("lead-summary")]
    [Authorize(Roles = "Provider,Admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetLeadSummary()
    {
        Guid supplierId;

        if (User.IsInRole("Admin"))
        {
            if (!Request.Query.TryGetValue("supplierId", out var sv) ||
                !Guid.TryParse(sv, out supplierId))
                return BadRequest(new { error = "Admin must specify ?supplierId=" });
        }
        else
        {
            var user = await db.Users.FindAsync(User.GetUserId());
            if (user?.SupplierId is null)
                return StatusCode(403, new { error = "Forbidden", message = "No supplier linked to your account", statusCode = 403 });
            supplierId = user.SupplierId.Value;
        }
        var now        = DateTime.UtcNow;
        var since30    = now.AddDays(-30);
        var since7     = now.AddDays(-7);

        var counts = await db.Orders
            .Where(o => o.SupplierId == supplierId && o.CreatedAt > since30)
            .GroupBy(o => o.LeadStatus)
            .Select(g => new { status = g.Key, count = g.Count() })
            .ToListAsync();

        var statusMap = counts.ToDictionary(x => x.status, x => x.count);

        var wonThisWeek  = await db.Orders
            .CountAsync(o => o.SupplierId == supplierId
                          && o.LeadStatus == LeadStatus.Won
                          && o.CreatedAt > since7);
        var lostThisWeek = await db.Orders
            .CountAsync(o => o.SupplierId == supplierId
                          && o.LeadStatus == LeadStatus.Lost
                          && o.CreatedAt > since7);

        return Ok(new
        {
            newCount       = statusMap.GetValueOrDefault(LeadStatus.New,       0),
            contactedCount = statusMap.GetValueOrDefault(LeadStatus.Contacted, 0),
            wonCount       = statusMap.GetValueOrDefault(LeadStatus.Won,       0),
            lostCount      = statusMap.GetValueOrDefault(LeadStatus.Lost,      0),
            wonThisWeek,
            lostThisWeek,
        });
    }
}

/// <summary>Simple request body for reject endpoint.</summary>
public record RejectOrderRequest(string Reason);
