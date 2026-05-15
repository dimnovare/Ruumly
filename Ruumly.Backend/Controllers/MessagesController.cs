using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/messages")]
[Authorize]
public class MessagesController(IMessageService messageService) : ControllerBase
{
    /// <summary>
    /// Returns all messages for a booking thread, ordered oldest → newest.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByBookingId([FromQuery] Guid bookingId)
    {
        var messages = await messageService.GetByBookingIdAsync(bookingId, User.GetUserId(), User.GetUserRole());
        return Ok(messages);
    }

    /// <summary>
    /// Sends a message in a booking thread.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting("user")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Send([FromBody] SendMessageRequest request)
    {
        var message = await messageService.SendAsync(
            request.BookingId, request, User.GetUserId(), User.GetUserRole());

        return CreatedAtAction(
            nameof(GetByBookingId),
            new { bookingId = message.BookingId },
            message);
    }

    /// <summary>
    /// Returns the total count of unread messages for the authenticated user.
    /// </summary>
    [HttpGet("unread-count")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnreadCount()
    {
        var userId = User.GetUserId();
        var role   = User.GetUserRole();
        var count  = await messageService.GetUnreadCountAsync(userId, role);
        return Ok(new { count });
    }

    /// <summary>
    /// Marks all messages from the other party as read in the given thread.
    /// </summary>
    [HttpPost("mark-read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead([FromQuery] Guid bookingId)
    {
        await messageService.MarkReadAsync(bookingId, User.GetUserId(), User.GetUserRole());
        return NoContent();
    }
}
