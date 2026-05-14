using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/contracts")]
[Authorize]
public class ContractController(
    RuumlyDbContext  db,
    IContractService contractService) : ControllerBase
{
    /// <summary>
    /// Returns active contract templates for the supplier linked to a booking.
    /// Called by the tenant before signing, or by admin/provider for preview.
    /// </summary>
    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplates([FromQuery] Guid bookingId)
    {
        var booking = await db.Bookings.FindAsync(bookingId);
        if (booking is null) return NotFound();

        var userId = User.GetUserId();
        if (booking.UserId != userId &&
            !User.IsInRole("Admin") &&
            !User.IsInRole("Provider"))
            return Forbid();

        var templates = await db.ContractTemplates
            .Where(t => t.SupplierId == booking.SupplierId && t.IsActive)
            .OrderByDescending(t => t.IsDefault)
            .ThenBy(t => t.Name)
            .ToListAsync();

        return Ok(templates.Select(t => new ContractTemplateDto(
            t.Id, t.Name, t.HtmlTemplate, t.IsActive, t.IsDefault,
            t.CreatedAt.ToString("o"), t.UpdatedAt.ToString("o"))));
    }

    /// <summary>
    /// Returns rendered HTML for display in an iframe — no signature required yet.
    /// </summary>
    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] PreviewContractRequest req)
    {
        var booking = await db.Bookings.FindAsync(req.BookingId);
        if (booking is null) return NotFound();

        var userId = User.GetUserId();
        if (booking.UserId != userId &&
            !User.IsInRole("Admin") &&
            !User.IsInRole("Provider"))
            return Forbid();

        try
        {
            var html = await contractService.RenderAsync(req.ContractTemplateId, req.BookingId);
            return Content(html, "text/html");
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Tenant signs the contract. One contract per booking (idempotent on retry).
    /// </summary>
    [HttpPost("sign")]
    [EnableRateLimiting("user")]
    public async Task<IActionResult> Sign([FromBody] SignContractRequest req)
    {
        var booking = await db.Bookings.FindAsync(req.BookingId);
        if (booking is null) return NotFound();
        if (booking.UserId != User.GetUserId()) return Forbid();

        var ip    = HttpContext.Connection.RemoteIpAddress?.ToString();
        var email = User.GetUserEmail();

        try
        {
            var signed = await contractService.SignAsync(req, email, ip);
            return Ok(new SignedContractDto(
                signed.Id, signed.BookingId, signed.RenderedHtml,
                signed.TenantName, signed.TenantIdCode, signed.TenantEmail,
                signed.SignedAt.ToString("o")));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Retrieves the signed contract snapshot for a completed booking.
    /// </summary>
    [HttpGet("booking/{bookingId:guid}")]
    public async Task<IActionResult> GetByBooking(Guid bookingId)
    {
        var booking = await db.Bookings.FindAsync(bookingId);
        if (booking is null) return NotFound();

        var userId = User.GetUserId();
        if (booking.UserId != userId &&
            !User.IsInRole("Admin") &&
            !User.IsInRole("Provider"))
            return Forbid();

        var contract = await db.SignedContracts
            .FirstOrDefaultAsync(c => c.BookingId == bookingId);
        if (contract is null) return NotFound();

        return Ok(new SignedContractDto(
            contract.Id, contract.BookingId, contract.RenderedHtml,
            contract.TenantName, contract.TenantIdCode, contract.TenantEmail,
            contract.SignedAt.ToString("o")));
    }
}
