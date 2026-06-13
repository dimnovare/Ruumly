using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/invoices")]
[Authorize]
public class InvoicesController(IInvoiceService invoiceService, RuumlyDbContext db) : ControllerBase
{
    /// <summary>
    /// Returns invoices for the current user (filtered by role).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int limit = 100)
    {
        var result = await invoiceService.GetAllAsync(User.GetUserId(), User.GetUserRole(), page, limit);
        return Ok(result);
    }

    /// <summary>
    /// Returns the invoice linked to a specific booking.
    /// </summary>
    [HttpGet("by-booking/{bookingId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByBookingId(Guid bookingId)
    {
        var invoice = await invoiceService.GetByBookingIdAsync(bookingId, User.GetUserId(), User.GetUserRole());
        if (invoice is null)
            return NotFound(new { error = "Not Found", message = "Invoice not found for this booking", statusCode = 404 });
        return Ok(invoice);
    }

    /// <summary>
    /// Returns only the id and status for the invoice linked to a booking.
    /// Lightweight alternative to the full GET when only payment status is needed.
    /// Allowed anonymously so a payment return can poll status even if the user
    /// got logged out during the redirect: the bookingId GUID is the capability.
    /// The anonymous path returns status-only (id + status) — no PII, amounts or
    /// customer data — and skips the role-based IDOR check (no user identity to check).
    /// Authenticated callers keep the existing ownership-checked path.
    /// </summary>
    [HttpGet("by-booking/{bookingId:guid}/status")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(Guid bookingId)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var invoice = await invoiceService.GetByBookingIdAsync(bookingId, User.GetUserId(), User.GetUserRole());
            if (invoice is null) return NotFound();
            return Ok(new { invoice.Id, invoice.Status });
        }

        // Anonymous: status-only projection straight from the DB, keyed solely by
        // the bookingId GUID. Selecting Id + Status guarantees no PII/amounts leak.
        var inv = await db.Invoices
            .Where(i => i.BookingId == bookingId)
            .Select(i => new { i.Id, i.Status })
            .FirstOrDefaultAsync();
        if (inv is null) return NotFound();
        // Match the authenticated/DTO shape: status as a lowercased string, not the
        // raw enum, so the frontend's string comparisons work on the anon path too.
        return Ok(new { inv.Id, Status = inv.Status.ToString().ToLowerInvariant() });
    }

    /// <summary>
    /// Manually generates (or returns existing) invoice for a booking.
    /// Useful for edge cases where auto-generation failed.
    /// </summary>
    [HttpPost("generate/{bookingId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Generate(Guid bookingId)
    {
        var userId  = User.GetUserId();
        var role    = User.GetUserRole();
        var booking = await db.Bookings.FindAsync(bookingId);
        if (booking is null) return NotFound();
        if (role != UserRole.Admin && booking.UserId != userId) return Forbid();

        var invoice = await invoiceService.GenerateAsync(bookingId);
        return Ok(invoice);
    }

    /// <summary>
    /// Marks an invoice as paid (called by payment webhook / admin).
    /// </summary>
    [HttpPost("{id:guid}/mark-paid")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkPaid(Guid id)
    {
        var invoice = await invoiceService.MarkPaidAsync(id);
        return Ok(invoice);
    }
}
