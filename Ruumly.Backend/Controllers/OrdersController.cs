using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/orders")]
[Authorize]
public class OrdersController(IOrderService orderService) : ControllerBase
{
    /// <summary>
    /// Returns orders. Admin sees all; Provider sees their supplier's orders; Customer sees their own.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin,Provider,Customer")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var result = await orderService.GetAllAsync(User.GetUserId(), User.GetUserRole());
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
        var order = await orderService.GetByIdAsync(id);
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
        var order = await orderService.GetByBookingIdAsync(bookingId);
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
        var order = await orderService.ApproveAsync(id, User.GetUserId());
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
        var order = await orderService.RejectAsync(id, body.Reason, User.GetUserId());
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
        var order = await orderService.ConfirmAsync(id, User.GetUserId());
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
        var order = await orderService.UpdateStatusAsync(id, request);
        return Ok(order);
    }
}

/// <summary>Simple request body for reject endpoint.</summary>
public record RejectOrderRequest(string Reason);
