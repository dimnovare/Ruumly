using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/bookings")]
[Authorize]
public class BookingsController(IBookingService bookingService) : ControllerBase
{
    /// <summary>
    /// Returns bookings for the current user (filtered by role).
    /// Customer sees own bookings; Admin/Provider see all.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        var userId = User.GetUserId();
        var role   = User.GetUserRole();
        var result = await bookingService.GetAllAsync(userId, role, page, limit);
        return Ok(result);
    }

    /// <summary>
    /// Returns a single booking by ID.
    /// Customer can only access their own bookings (403 if not owner).
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var userId  = User.GetUserId();
        var role    = User.GetUserRole();
        var booking = await bookingService.GetByIdAsync(id, userId, role);
        if (booking is null)
            return NotFound(new { error = "Not Found", message = "Booking not found", statusCode = 404 });
        return Ok(booking);
    }

    /// <summary>
    /// Cancels a booking and its linked order (soft-delete).
    /// Customer can only cancel their own bookings.
    /// </summary>
    [HttpPatch("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var userId  = User.GetUserId();
        var role    = User.GetUserRole();
        var booking = await bookingService.CancelAsync(id, userId, role);
        return Ok(booking);
    }

    /// <summary>
    /// Returns the extras price configuration. Public — no auth required.
    /// Frontend uses this as the single source of truth for extras pricing.
    /// </summary>
    [HttpGet("extras-config")]
    [AllowAnonymous]
    public IActionResult GetExtrasConfig() =>
        Ok(bookingService.GetExtrasPrices());

    /// <summary>
    /// Creates a new booking, routes an order, and dispatches to the supplier.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] CreateBookingRequest request)
    {
        var userId  = User.GetUserId();
        var booking = await bookingService.CreateAsync(request, userId);
        return CreatedAtAction(nameof(GetById), new { id = booking.Id }, booking);
    }
}
