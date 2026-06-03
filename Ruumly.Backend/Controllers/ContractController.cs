using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Models;
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
    IContractService contractService,
    IDokobitService  dokobitService,
    IConfiguration   configuration) : ControllerBase
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

        if (!await CanAccessBookingAsync(booking))
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

        if (!await CanAccessBookingAsync(booking))
            return Forbid();

        try
        {
            var html = await contractService.RenderAsync(req.ContractTemplateId, req.BookingId);
            return Ok(new { html });
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

        if (!await CanAccessBookingAsync(booking))
            return Forbid();

        var contract = await db.SignedContracts
            .FirstOrDefaultAsync(c => c.BookingId == bookingId);
        if (contract is null) return NotFound();

        return Ok(new SignedContractDto(
            contract.Id, contract.BookingId, contract.RenderedHtml,
            contract.TenantName, contract.TenantIdCode, contract.TenantEmail,
            contract.SignedAt.ToString("o")));
    }

    // ─── Dokobit e-signature endpoints ───────────────────────────────────────

    /// <summary>
    /// Returns whether Dokobit e-signing is available on this deployment.
    /// No auth required — used by the frontend to decide which signing UI to render.
    /// </summary>
    [HttpGet("signing-method")]
    [AllowAnonymous]
    public IActionResult GetSigningMethod()
        => Ok(new { dokobitEnabled = dokobitService.IsEnabled });

    /// <summary>
    /// Initiates a Dokobit e-signing session for a booking's contract.
    /// Renders the contract HTML, uploads it to Dokobit, creates a signing
    /// request, persists a pending SignedContract, and returns the signing URL.
    /// </summary>
    [HttpPost("dokobit/initiate")]
    [EnableRateLimiting("user")]
    public async Task<IActionResult> InitiateDokobitSigning(
        [FromBody] InitiateDokobitSigningRequest req,
        CancellationToken ct)
    {
        if (!dokobitService.IsEnabled)
            return BadRequest(new { error = "Dokobit e-signing is not configured on this deployment." });

        var booking = await db.Bookings.FindAsync([req.BookingId], ct);
        if (booking is null) return NotFound();
        if (booking.UserId != User.GetUserId()) return Forbid();

        // Render contract HTML
        string renderedHtml;
        try
        {
            renderedHtml = await contractService.RenderAsync(req.ContractTemplateId, req.BookingId, ct);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }

        // Substitute signer identity fields into the rendered HTML
        renderedHtml = renderedHtml
            .Replace("{{tenant_name}}",    System.Web.HttpUtility.HtmlEncode(req.SignerName))
            .Replace("{{tenant_id_code}}", System.Web.HttpUtility.HtmlEncode(req.SignerIdCode));

        // Upload the HTML file to Dokobit (Dokobit Gateway accepts HTML documents)
        var fileName  = $"contract-{req.BookingId:N}.html";
        var fileBytes = Encoding.UTF8.GetBytes(renderedHtml);
        var upload    = await dokobitService.UploadDocumentAsync(fileName, fileBytes, ct);
        if (!upload.Success)
            return StatusCode(502, new { error = $"Dokobit upload failed: {upload.Error}" });

        // Build the return URL from SITE_URL env var (or fallback)
        var siteUrl    = configuration["SiteUrl"] ?? "https://ruumly.eu";
        var returnUrl  = $"{siteUrl.TrimEnd('/')}/et/booking/{req.BookingId}/contract/complete";

        var signing = await dokobitService.CreateSigningRequestAsync(
            upload.Token, req.SignerName, req.SignerIdCode, req.SignerEmail, returnUrl, ct);
        if (!signing.Success)
            return StatusCode(502, new { error = $"Dokobit signing/create failed: {signing.Error}" });

        // SHA-256 of rendered HTML for tamper-evidence
        var hashBytes = SHA256.HashData(fileBytes);
        var htmlHash  = Convert.ToHexString(hashBytes).ToLowerInvariant();

        // Persist a pending SignedContract record
        var pending = new SignedContract
        {
            BookingId           = req.BookingId,
            ContractTemplateId  = req.ContractTemplateId,
            RenderedHtml        = renderedHtml,
            RenderedHtmlHash    = htmlHash,
            SignatureDataUrl    = "",          // not applicable for Dokobit path
            TenantName          = req.SignerName,
            TenantIdCode        = req.SignerIdCode,
            TenantEmail         = req.SignerEmail,
            SignedFromIp        = HttpContext.Connection.RemoteIpAddress?.ToString(),
            DokobitSigningToken = signing.SigningToken,
            // Signing method will be updated to "smartid"/"mobileid"/"idcard" when
            // Dokobit returns signer info on completion. Set a sentinel for now.
            SigningMethod       = "dokobit",
            Status              = "pending",
            SignedAt            = DateTime.UtcNow,
            CreatedAt           = DateTime.UtcNow,
        };
        db.SignedContracts.Add(pending);
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            signingUrl   = signing.SigningUrl,
            signingToken = signing.SigningToken,
        });
    }

    /// <summary>
    /// Polls Dokobit for the signing status. When completed, downloads the
    /// signed document (future: store to R2) and marks the contract as signed.
    /// </summary>
    [HttpGet("dokobit/{signingToken}/status")]
    public async Task<IActionResult> DokobitStatus(string signingToken, CancellationToken ct)
    {
        if (!dokobitService.IsEnabled)
            return BadRequest(new { error = "Dokobit is not configured." });

        // Look up the pending contract
        var contract = await db.SignedContracts
            .FirstOrDefaultAsync(c => c.DokobitSigningToken == signingToken, ct);
        if (contract is null) return NotFound(new { error = "Signing session not found." });

        // Only the booking owner (or admin) may poll
        var booking = await db.Bookings.FindAsync([contract.BookingId], ct);
        if (booking is null) return NotFound();
        if (!await CanAccessBookingAsync(booking)) return Forbid();

        // If already terminal, return cached status
        if (contract.Status is "completed" or "cancelled" or "error")
            return Ok(new { status = contract.Status });

        var dokobitStatus = await dokobitService.GetStatusAsync(signingToken, ct);

        switch (dokobitStatus)
        {
            case DokobitSigningStatus.Completed:
            {
                // Download the signed document — stored bytes reserved for future R2 upload.
                // For now we just confirm completion and update the record.
                // Dokobit's status response does not include signer method in the basic API;
                // we update SigningMethod to "dokobit" (already set) as a record.
                contract.Status   = "completed";
                contract.SignedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return Ok(new { status = "completed" });
            }
            case DokobitSigningStatus.Cancelled:
                contract.Status = "cancelled";
                await db.SaveChangesAsync(ct);
                return Ok(new { status = "cancelled" });

            case DokobitSigningStatus.Error:
                contract.Status = "error";
                await db.SaveChangesAsync(ct);
                return Ok(new { status = "error" });

            default:
                return Ok(new { status = "pending" });
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// True if the calling user is allowed to read this booking's contract data.
    /// Admin always passes; the booking's customer always passes; a Provider
    /// passes only if their supplier owns the booking.
    /// </summary>
    private async Task<bool> CanAccessBookingAsync(Booking booking)
    {
        if (User.IsInRole("Admin")) return true;
        if (booking.UserId == User.GetUserId()) return true;
        if (User.IsInRole("Provider"))
        {
            var supplierId = await db.Users
                .Where(u => u.Id == User.GetUserId())
                .Select(u => u.SupplierId)
                .FirstOrDefaultAsync();
            return supplierId == booking.SupplierId;
        }
        return false;
    }
}
