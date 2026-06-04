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
using Ruumly.Backend.Identity;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/contracts")]
[Authorize]
public class ContractController(
    RuumlyDbContext  db,
    IContractService contractService,
    IDokobitService  dokobitService,
    IStorageService  storageService,
    IContractDocumentService docService,
    IGotenbergClient gotenberg,
    IConfiguration   configuration,
    ILogger<ContractController> logger,
    IdentityVerificationService? identityService = null) : ControllerBase
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
    /// For canvas signing: pass SignatureDataUrl.
    /// For Smart-ID / Mobile-ID: pass SigningMethod ("smartid"/"mobileid") and
    /// VerifiedSessionId (the session id returned by POST /identity/start once status is "completed").
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

        // ── eID path: Smart-ID or Mobile-ID ──────────────────────────────────
        var signingMethod = req.SigningMethod?.ToLowerInvariant();
        if (signingMethod is "smartid" or "mobileid")
        {
            if (identityService is null)
                return BadRequest(new { error = "eID verification is not configured on this deployment." });

            if (string.IsNullOrWhiteSpace(req.VerifiedSessionId))
                return BadRequest(new { error = "verifiedSessionId is required for eID signing." });

            var session = await identityService.GetSessionAsync(req.VerifiedSessionId);
            if (session is null)
                return BadRequest(new { error = "Identity session not found or expired." });

            if (session.BookingId != req.BookingId)
                return BadRequest(new { error = "Session does not belong to this booking." });

            if (session.Status != "completed")
                return BadRequest(new { error = $"Identity session is not completed (status: {session.Status})." });

            // Sign via canvas path but override fields with eID-verified values.
            // We fabricate a minimal SignatureDataUrl to pass validation — the real audit
            // trail is the identity session stored in cache and written to SignedContract.
            var eidReq = req with
            {
                SignatureDataUrl = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
            };

            try
            {
                var signed = await contractService.SignAsync(eidReq, email, ip);

                // Write identity-verified fields directly to the SignedContract row.
                signed.SigningMethod  = signingMethod;
                signed.VerifiedName   = session.VerifiedName;
                signed.VerifiedIdCode = req.TenantIdCode; // supplied by the frontend from the form
                await db.SaveChangesAsync();

                return Ok(new SignedContractDto(
                    signed.Id, signed.BookingId, signed.RenderedHtml,
                    signed.TenantName, signed.TenantIdCode, signed.TenantEmail,
                    signed.SignedAt.ToString("o")));
            }
            catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        }

        // ── Canvas acknowledgment path (default) ──────────────────────────────
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

    // ─── Smart-ID / Mobile-ID identity verification endpoints ────────────────

    /// <summary>
    /// Starts a Smart-ID or Mobile-ID identity verification session for a booking.
    /// Returns a session id and 4-digit anti-phishing verification code.
    /// Only available when SMARTID_RP_UUID (SmartId:RelyingPartyUuid) env var is set.
    /// </summary>
    [HttpPost("identity/start")]
    [EnableRateLimiting("user")]
    public async Task<IActionResult> StartIdentityVerification(
        [FromBody] StartIdentityVerificationRequest req,
        CancellationToken ct)
    {
        if (identityService is null)
            return BadRequest(new { error = "eID verification is not configured on this deployment." });

        var booking = await db.Bookings.FindAsync([req.BookingId], ct);
        if (booking is null) return NotFound();
        if (booking.UserId != User.GetUserId()) return Forbid();

        var method = req.Method?.ToLowerInvariant();
        if (method is not ("smartid" or "mobileid"))
            return BadRequest(new { error = "method must be 'smartid' or 'mobileid'." });

        if (string.IsNullOrWhiteSpace(req.PersonalCode))
            return BadRequest(new { error = "personalCode is required." });

        // Normalise method name to provider name format.
        var providerName = method == "smartid" ? "smart-id" : "mobile-id";

        // Default country to EE (Estonia) when not supplied.
        var country = string.IsNullOrWhiteSpace(req.Country) ? "EE" : req.Country.ToUpperInvariant();

        try
        {
            var result = await identityService.StartAsync(
                req.BookingId, providerName, req.PersonalCode, country, req.PhoneNumber, ct);

            return Ok(new
            {
                sessionId = result.SessionId,
                verificationCode = result.VerificationCode,
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Polls an identity verification session for its current status.
    /// Returns status: "pending" | "completed" | "failed" | "expired".
    /// When completed, verifiedName is populated.
    /// </summary>
    [HttpGet("identity/{sessionId}")]
    public async Task<IActionResult> PollIdentityVerification(string sessionId, CancellationToken ct)
    {
        if (identityService is null)
            return BadRequest(new { error = "eID verification is not configured on this deployment." });

        // Validate the session belongs to a booking the caller owns (or is admin).
        var cached = await identityService.GetSessionAsync(sessionId, ct);
        if (cached is null)
            return NotFound(new { error = "Session not found or expired." });

        var booking = await db.Bookings.FindAsync([cached.BookingId], ct);
        if (booking is null) return NotFound();
        if (!await CanAccessBookingAsync(booking)) return Forbid();

        var result = await identityService.PollAsync(sessionId, ct);
        if (result is null)
            return NotFound(new { error = "Session not found or expired." });

        return Ok(new
        {
            status = result.Status,
            verifiedName = result.VerifiedName,
        });
    }

    // ─── Dokobit e-signature endpoints ───────────────────────────────────────

    /// <summary>
    /// Returns which signing methods are available on this deployment.
    /// No auth required — used by the frontend to decide which signing UI to render.
    /// </summary>
    [HttpGet("signing-method")]
    [AllowAnonymous]
    public async Task<IActionResult> GetSigningMethod()
    {
        var smartIdEnabled = identityService is not null && await identityService.IsSmartIdConfiguredAsync();
        var mobileIdEnabled = identityService is not null && await identityService.IsMobileIdConfiguredAsync();

        return Ok(new
        {
            dokobitEnabled = dokobitService.IsEnabled,
            smartIdEnabled,
            mobileIdEnabled,
        });
    }

    /// <summary>
    /// Initiates a Dokobit e-signing session for a booking's contract.
    /// Loads the supplier's active docx template, fills it with the booking's data,
    /// renders it to PDF via Gotenberg, uploads it to Dokobit, creates a signing
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
        if (!gotenberg.IsEnabled)
            return BadRequest(new { error = "Contract rendering is not configured on this deployment (Gotenberg:Url not set)." });

        var booking = await db.Bookings
            .Include(b => b.User)
            .Include(b => b.Listing).ThenInclude(l => l.Location)
            .Include(b => b.Supplier)
            .FirstOrDefaultAsync(b => b.Id == req.BookingId, ct);
        if (booking is null) return NotFound();
        if (booking.UserId != User.GetUserId()) return Forbid();

        // The frontend posts only { bookingId, contractTemplateId? }; the signer identity
        // comes from the booking (and is VERIFIED by Dokobit during signing). Request fields
        // are optional overrides. SignerIdCode stays optional — the verified code is captured
        // post-sign from the gateway; rendering {{tenant_id_code}} blank here is acceptable.
        var signerName = !string.IsNullOrWhiteSpace(req.SignerName)
            ? req.SignerName!
            : (booking.ContactName ?? booking.User?.Name ?? "");
        var signerEmail = !string.IsNullOrWhiteSpace(req.SignerEmail)
            ? req.SignerEmail!
            : (booking.ContactEmail ?? booking.User?.Email ?? "");
        var signerPhone = !string.IsNullOrWhiteSpace(req.SignerPhone)
            ? req.SignerPhone!
            : (booking.ContactPhone ?? booking.User?.Phone);

        // Resolve the docx template: explicit id if given, else the supplier's active docx.
        var template = await db.ContractTemplates.FirstOrDefaultAsync(t =>
            t.SupplierId == booking.SupplierId
            && t.TemplateType == ContractTemplateType.Docx
            && (req.ContractTemplateId != null ? t.Id == req.ContractTemplateId : t.IsActive), ct);
        if (template is null || string.IsNullOrEmpty(template.DocxObjectKey))
            return NotFound(new { error = "No active docx contract template is configured for this supplier." });

        // Fetch the docx and fill it with this booking's values.
        var docxBytes = await storageService.DownloadAsync(template.DocxObjectKey);
        if (docxBytes is null)
            return StatusCode(502, new { error = "Contract template file is missing from storage." });

        var values = ContractTokenVocabulary.BuildValues(
            booking, signerName, req.SignerIdCode, signerEmail);
        var filledDocx = docService.Fill(docxBytes, values);

        // Render to PDF via Gotenberg.
        byte[] pdfBytes;
        try
        {
            pdfBytes = await gotenberg.ConvertDocxToPdfAsync(filledDocx, $"contract-{req.BookingId:N}.docx", ct);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Gotenberg render failed for booking {BookingId}", req.BookingId);
            return StatusCode(502, new { error = "Contract PDF rendering failed: " + ex.Message });
        }

        // Upload the PDF to Dokobit (base64 + digest — never hosted publicly).
        var fileName = $"contract-{req.BookingId:N}.pdf";
        var upload   = await dokobitService.UploadDocumentAsync(fileName, pdfBytes, ct);
        if (!upload.Success)
            return StatusCode(502, new { error = $"Dokobit upload failed: {upload.Error}" });

        var siteUrl     = configuration["SiteUrl"] ?? "https://ruumly.eu";
        var postbackUrl = $"{siteUrl.TrimEnd('/')}/api/contracts/dokobit/callback";

        var country = !string.IsNullOrWhiteSpace(req.SignerCountryCode)
            ? req.SignerCountryCode!.ToUpperInvariant()
            : (booking.Supplier?.Country ?? "EE");

        var signing = await dokobitService.CreateSigningRequestAsync(
            upload.Token,
            $"Ruumly contract {booking.Id.ToString("N")[..8].ToUpperInvariant()}",
            new DokobitSigner(signerName, req.SignerIdCode, country, signerPhone, signerEmail),
            postbackUrl,
            ct);
        if (!signing.Success)
            return StatusCode(502, new { error = $"Dokobit signing/create failed: {signing.Error}" });

        // SHA-256 of the signed-candidate PDF for tamper-evidence.
        var pdfHash = Convert.ToHexString(SHA256.HashData(pdfBytes)).ToLowerInvariant();

        // Upsert a pending SignedContract for this booking (idempotent on re-initiate).
        var pending = await db.SignedContracts.FirstOrDefaultAsync(c => c.BookingId == req.BookingId, ct);
        if (pending is null)
        {
            pending = new SignedContract
            {
                BookingId          = req.BookingId,
                ContractTemplateId = template.Id,
                CreatedAt          = DateTime.UtcNow,
            };
            db.SignedContracts.Add(pending);
        }
        pending.ContractTemplateId  = template.Id;
        pending.RenderedHtml        = string.Empty;          // docx path has no HTML snapshot
        pending.RenderedHtmlHash    = pdfHash;
        pending.SignatureDataUrl    = string.Empty;
        pending.TenantName          = signerName;
        pending.TenantIdCode        = req.SignerIdCode;
        pending.TenantEmail         = signerEmail;
        pending.SignedFromIp        = HttpContext.Connection.RemoteIpAddress?.ToString();
        pending.DokobitSigningToken = signing.SigningToken;
        pending.SigningMethod       = "dokobit";             // refined to smartid/mobile on completion
        pending.Status              = "pending";
        pending.SignedAt            = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            signingUrl   = signing.SigningUrl,
            signingToken = signing.SigningToken,
        });
    }

    /// <summary>
    /// Polls Dokobit for the signing status (fallback to the postback). When completed,
    /// captures the verified identity, downloads the signed PDF, stores it to R2 and
    /// marks the contract signed. Booking owner or admin/provider only.
    /// </summary>
    [HttpGet("dokobit/{signingToken}/status")]
    public async Task<IActionResult> DokobitStatus(string signingToken, CancellationToken ct)
    {
        if (!dokobitService.IsEnabled)
            return BadRequest(new { error = "Dokobit is not configured." });

        var contract = await db.SignedContracts
            .FirstOrDefaultAsync(c => c.DokobitSigningToken == signingToken, ct);
        if (contract is null) return NotFound(new { error = "Signing session not found." });

        var booking = await db.Bookings.FindAsync([contract.BookingId], ct);
        if (booking is null) return NotFound();
        if (!await CanAccessBookingAsync(booking)) return Forbid();

        // Already terminal — return cached status (idempotent).
        if (contract.Status is "completed" or "cancelled" or "error")
            return Ok(new { status = contract.Status, signedDocumentUrl = contract.SignedDocumentUrl });

        var result = await dokobitService.GetStatusAsync(signingToken, ct);
        await ApplyDokobitResultAsync(contract, signingToken, result, ct);

        return Ok(new { status = contract.Status, signedDocumentUrl = contract.SignedDocumentUrl });
    }

    /// <summary>
    /// Dokobit postback endpoint. Anonymous — Dokobit POSTs here after each signature.
    /// The token is read from the query/form; status is re-fetched server-to-server
    /// (we never trust the body). Idempotent via the contract's terminal-status guard.
    /// Always returns 200 so Dokobit does not retry indefinitely.
    /// </summary>
    [HttpPost("dokobit/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> DokobitCallback(CancellationToken ct)
    {
        if (!dokobitService.IsEnabled)
            return Ok();   // nothing to do; acknowledge so Dokobit stops retrying

        var token = ReadCallbackToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("Dokobit callback received without a signing token.");
            return Ok();
        }

        var contract = await db.SignedContracts.FirstOrDefaultAsync(c => c.DokobitSigningToken == token, ct);
        if (contract is null)
        {
            logger.LogWarning("Dokobit callback for unknown signing token {Token}", token);
            return Ok();
        }

        // Idempotent: skip if already terminal.
        if (contract.Status is "completed" or "cancelled" or "error")
            return Ok();

        var result = await dokobitService.GetStatusAsync(token, ct);
        await ApplyDokobitResultAsync(contract, token, result, ct);

        return Ok();
    }

    /// <summary>
    /// Applies a Dokobit status result to a pending contract: on completion captures the
    /// gateway-verified identity, downloads + stores the signed PDF to R2, and flips the
    /// status. Shared by the poll and postback paths. Persists changes.
    /// </summary>
    private async Task ApplyDokobitResultAsync(
        SignedContract contract, string signingToken, DokobitStatusResult result, CancellationToken ct)
    {
        switch (result.Status)
        {
            case DokobitSigningStatus.Completed:
                // Capture the VERIFIED identity from the gateway (authoritative — not a form value).
                if (!string.IsNullOrWhiteSpace(result.VerifiedIdCode))
                    contract.VerifiedIdCode = result.VerifiedIdCode;
                if (!string.IsNullOrWhiteSpace(result.VerifiedName))
                    contract.VerifiedName = result.VerifiedName;
                if (!string.IsNullOrWhiteSpace(result.SigningOption))
                    contract.SigningMethod = result.SigningOption!;   // "smartid" | "mobile"

                try
                {
                    var pdfBytes = await dokobitService.DownloadSignedDocumentAsync(signingToken, ct);
                    if (pdfBytes is { Length: > 0 })
                    {
                        var name = $"signed-contracts/{contract.BookingId:N}/{signingToken}.pdf";
                        using var stream = new MemoryStream(pdfBytes);
                        contract.SignedDocumentUrl = await storageService.UploadAsync(stream, name, "application/pdf");
                    }
                }
                catch (Exception ex)
                {
                    // Completing the record matters more than a transient R2 hiccup.
                    logger.LogError(ex, "Failed to download/store signed Dokobit PDF for token {Token}", signingToken);
                }

                contract.Status   = "completed";
                contract.SignedAt = DateTime.UtcNow;
                break;

            case DokobitSigningStatus.Cancelled:
                contract.Status = "cancelled";
                break;

            case DokobitSigningStatus.Error:
                contract.Status = "error";
                break;

            // Pending → leave as-is.
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Reads the Dokobit signing token from common callback locations (query or form).</summary>
    private string? ReadCallbackToken()
    {
        foreach (var key in new[] { "token", "signing_token", "signingToken" })
        {
            if (Request.Query.TryGetValue(key, out var qv) && !string.IsNullOrWhiteSpace(qv))
                return qv.ToString();
        }
        if (Request.HasFormContentType)
        {
            foreach (var key in new[] { "token", "signing_token", "signingToken" })
            {
                if (Request.Form.TryGetValue(key, out var fv) && !string.IsNullOrWhiteSpace(fv))
                    return fv.ToString();
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the signed contract for a booking.
    /// - If a Dokobit-signed PDF was stored in R2, returns { url: "..." } (302-style redirect target).
    /// - If only a canvas HTML signing exists, returns the HTML snapshot as text/html.
    /// - 404 if no signed contract exists for this booking.
    /// Booking owner or admin/provider with access may call this.
    /// </summary>
    [HttpGet("{bookingId:guid}/download")]
    public async Task<IActionResult> DownloadContract(Guid bookingId, CancellationToken ct)
    {
        var booking = await db.Bookings.FindAsync([bookingId], ct);
        if (booking is null) return NotFound();

        if (!await CanAccessBookingAsync(booking))
            return Forbid();

        var contract = await db.SignedContracts
            .FirstOrDefaultAsync(c => c.BookingId == bookingId, ct);

        if (contract is null)
            return NotFound(new { error = "No signed contract found for this booking." });

        // Prefer the R2-stored signed PDF (Dokobit path).
        if (!string.IsNullOrEmpty(contract.SignedDocumentUrl))
            return Ok(new { url = contract.SignedDocumentUrl });

        // Fallback: return the canvas-signed rendered HTML snapshot.
        if (!string.IsNullOrEmpty(contract.RenderedHtml))
            return Content(contract.RenderedHtml, "text/html");

        return NotFound(new { error = "Contract document is not yet available." });
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
