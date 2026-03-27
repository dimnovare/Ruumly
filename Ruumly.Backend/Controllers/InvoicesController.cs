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
    public async Task<IActionResult> GetAll()
    {
        var result = await invoiceService.GetAllAsync(User.GetUserId(), User.GetUserRole());
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
