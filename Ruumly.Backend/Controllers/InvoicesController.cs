using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/invoices")]
[Authorize]
public class InvoicesController(IInvoiceService invoiceService) : ControllerBase
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
