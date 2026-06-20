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

        // No supplier template → expose the in-code platform default so the frontend modal shows a
        // signable contract instead of dead-ending. The preview re-renders server-side from the
        // sentinel id (the Html field here is not used to render).
        if (templates.Count == 0)
        {
            var now = DateTime.UtcNow.ToString("o");
            return Ok(new[]
            {
                new ContractTemplateDto(
                    PlatformDefaultContract.TemplateId, PlatformDefaultContract.Name,
                    string.Empty, true, true, now, now),
            });
        }

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
                signed.VerifiedName   = session.VerifiedName; // authoritative — from the eID certificate
                // VerifiedIdCode is deliberately left null on the eID path. The Smart-ID/Mobile-ID
                // session only exposes an HMAC PersonalCodeHash, never the raw personal code, so no
                // gateway-verified id code is available here. We must NOT promote req.TenantIdCode (a
                // client-supplied form value) to the legally "verified" field — that would be a
                // non-repudiation hole. VerifiedIdCode is only ever populated from a gateway-verified
                // source (the Dokobit completion path). The unverified client value remains in the
                // separate TenantIdCode field (set by ContractService.SignAsync).
                await db.SaveChangesAsync();

                return Ok(new SignedContractDto(
                    signed.Id, signed.BookingId, signed.RenderedHtml,
                    signed.TenantName, signed.TenantIdCode, signed.TenantEmail,
                    signed.SignedAt.ToString("o"),
                    signed.SigningMethod, !string.IsNullOrEmpty(signed.VerifiedIdCode)));
            }
            catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
            catch (Exception ex) when (ex is DbUpdateException or DbUpdateConcurrencyException)
            {
                // Concurrent/duplicate eID sign for the same booking lost the race on the
                // one-contract-per-booking constraint. The check-then-INSERT in SignAsync is not
                // atomic, so the loser surfaces a constraint violation here. Re-read the contract
                // the winner committed and return it (idempotent) instead of a 500. AsNoTracking
                // sidesteps the identity-map conflict left by the failed SaveChanges on `db`.
                logger.LogWarning(ex,
                    "Concurrent eID signing for booking {BookingId}; returning the already-signed contract.",
                    req.BookingId);

                var winner = await db.SignedContracts.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.BookingId == req.BookingId);
                if (winner is not null)
                    return Ok(new SignedContractDto(
                        winner.Id, winner.BookingId, winner.RenderedHtml,
                        winner.TenantName, winner.TenantIdCode, winner.TenantEmail,
                        winner.SignedAt.ToString("o"),
                        winner.SigningMethod, !string.IsNullOrEmpty(winner.VerifiedIdCode)));

                return Conflict(new { error = "Contract signing is already in progress for this booking." });
            }
        }

        // ── Identity-assurance guard ──────────────────────────────────────────
        // When qualified eID signing is configured (Dokobit and/or Smart-ID/Mobile-ID),
        // the server must NOT accept an unverified canvas acknowledgment as a completed,
        // payment-clearing contract. The web UI only offers eID in that case, but a
        // scripted client could POST signingMethod=canvas with a self-declared id code
        // and a 1x1 PNG to skip qualified signing — close that off server-side. Canvas
        // remains valid only on deployments with no eID provider configured.
        if (dokobitService.IsEnabled || identityService is not null)
            return BadRequest(new { error = "A qualified e-signature is required. Please complete signing via the eID flow." });

        // ── Canvas acknowledgment path (default) ──────────────────────────────
        try
        {
            var signed = await contractService.SignAsync(req, email, ip);
            return Ok(new SignedContractDto(
                signed.Id, signed.BookingId, signed.RenderedHtml,
                signed.TenantName, signed.TenantIdCode, signed.TenantEmail,
                signed.SignedAt.ToString("o"),
                signed.SigningMethod, !string.IsNullOrEmpty(signed.VerifiedIdCode)));
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
            contract.SignedAt.ToString("o"),
            contract.SigningMethod, !string.IsNullOrEmpty(contract.VerifiedIdCode)));
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
        // Skip the supplier lookup entirely when the caller explicitly requests the platform default.
        var template = req.ContractTemplateId == PlatformDefaultContract.TemplateId
            ? null
            : await db.ContractTemplates.FirstOrDefaultAsync(t =>
                t.SupplierId == booking.SupplierId
                && t.TemplateType == ContractTemplateType.Docx
                && (req.ContractTemplateId != null ? t.Id == req.ContractTemplateId : t.IsActive), ct);

        // The template id we stamp onto the SignedContract (real template or the platform-default sentinel).
        Guid contractTemplateId;
        byte[] docxBytes;

        if (template is not null && !string.IsNullOrEmpty(template.DocxObjectKey))
        {
            // Fetch the supplier's uploaded docx and fill it with this booking's values.
            var downloaded = await storageService.DownloadAsync(template.DocxObjectKey);
            if (downloaded is null)
                return StatusCode(502, new { error = "Contract template file is missing from storage." });
            docxBytes          = downloaded;
            contractTemplateId = template.Id;
        }
        else
        {
            // PROD-CRITICAL fallback: no supplier docx template → build the in-code platform default.
            // The built docx carries {{token}} placeholders that the existing Fill pipeline resolves,
            // so the rest of the flow (Fill → AppendClause → Gotenberg → Dokobit) is unchanged.
            docxBytes          = docService.BuildDocx(PlatformDefaultContract.Paragraphs);
            contractTemplateId = PlatformDefaultContract.TemplateId;
        }

        // Sign-then-pay: the rental is conditional on payment within the configured window.
        // Resolve the same window the auto-void job uses so the contract text and the
        // safeguard agree.
        var paymentConditionHours = await ResolvePaymentConditionHoursAsync(ct);

        var values = ContractTokenVocabulary.BuildValues(
            booking, signerName, req.SignerIdCode, signerEmail,
            paymentConditionHours: paymentConditionHours);
        var filledDocx = docService.Fill(docxBytes, values);

        // Always append the conditional-on-payment clause as a standard preamble/footer so it
        // binds even when the provider's template omits {{payment_condition_clause}}. A
        // signed-but-unpaid contract must state it binds no one.
        filledDocx = docService.AppendClause(
            filledDocx, ContractTokenVocabulary.PaymentConditionClause(paymentConditionHours));

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
                ContractTemplateId = contractTemplateId,
                CreatedAt          = DateTime.UtcNow,
            };
            db.SignedContracts.Add(pending);
        }
        pending.ContractTemplateId  = contractTemplateId;
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

        // A completed Dokobit row is terminal only after its legal artifacts were persisted.
        // Older/incomplete rows are deliberately reprocessed so transient failures self-heal.
        if ((contract.Status == "completed" && HasDokobitArtifacts(contract))
            || contract.Status is "cancelled" or "error")
            return Ok(new { status = contract.Status, hasSignedDocument = !string.IsNullOrWhiteSpace(contract.SignedDocumentUrl) });

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
    [EnableRateLimiting("dokobit")]
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

        // Idempotent only when completion includes the verified identity and signed PDF.
        if ((contract.Status == "completed" && HasDokobitArtifacts(contract))
            || contract.Status is "cancelled" or "error")
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

                if (string.IsNullOrWhiteSpace(contract.SignedDocumentUrl))
                {
                    try
                    {
                        var pdfBytes = await dokobitService.DownloadSignedDocumentAsync(signingToken, ct);
                        if (pdfBytes is { Length: > 0 })
                        {
                            var name = $"signed-contracts/{contract.BookingId:N}/{signingToken}.pdf";
                            using var stream = new MemoryStream(pdfBytes);
                            // Store the object KEY (not a public URL). The signed PDF contains
                            // verified national-ID PII; it must only ever be read back through
                            // the auth-gated download endpoint, never via a public URL.
                            var stored = await storageService.UploadWithKeyAsync(stream, name, "application/pdf");
                            contract.SignedDocumentUrl = stored.Key;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to download/store signed Dokobit PDF for token {Token}", signingToken);
                    }
                }

                if (HasDokobitArtifacts(contract))
                {
                    contract.Status   = "completed";
                    contract.SignedAt = DateTime.UtcNow;
                }
                else
                {
                    contract.Status = "pending";
                    logger.LogWarning(
                        "Dokobit signing {Token} is complete at the gateway but artifacts are incomplete. " +
                        "VerifiedId={HasId}, VerifiedName={HasName}, SignedDocument={HasDocument}; will retry.",
                        signingToken,
                        !string.IsNullOrWhiteSpace(contract.VerifiedIdCode),
                        !string.IsNullOrWhiteSpace(contract.VerifiedName),
                        !string.IsNullOrWhiteSpace(contract.SignedDocumentUrl));
                }
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

    private static bool HasDokobitArtifacts(SignedContract contract) =>
        !string.IsNullOrWhiteSpace(contract.VerifiedIdCode)
        && !string.IsNullOrWhiteSpace(contract.VerifiedName)
        && !string.IsNullOrWhiteSpace(contract.SignedDocumentUrl);

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

        // Prefer the R2-stored signed PDF (Dokobit path). Stream the bytes back
        // THROUGH this auth-gated endpoint — never hand out a public R2 URL, since
        // the PDF carries verified national-ID PII. SignedDocumentUrl now holds the
        // object KEY; legacy rows may hold a full public URL, which we reduce to a key.
        if (!string.IsNullOrEmpty(contract.SignedDocumentUrl))
        {
            var key = ResolveStorageKey(contract.SignedDocumentUrl);
            var bytes = await storageService.DownloadAsync(key);
            if (bytes is { Length: > 0 })
                return File(bytes, "application/pdf", $"contract-{bookingId:N}.pdf");
            // PDF missing/unreadable — fall through to the HTML snapshot if present.
        }

        // Fallback: return the canvas-signed rendered HTML snapshot.
        if (!string.IsNullOrEmpty(contract.RenderedHtml))
            return Content(contract.RenderedHtml, "text/html");

        return NotFound(new { error = "Contract document is not yet available." });
    }

    /// <summary>
    /// Reduces a stored SignedDocumentUrl to an R2 object key. New rows store the
    /// key directly; older rows may hold a full public URL — strip the public base.
    /// </summary>
    private string ResolveStorageKey(string stored)
    {
        if (!stored.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return stored;
        var publicBase = (configuration["Storage:R2PublicUrl"] ?? "").TrimEnd('/') + "/";
        if (publicBase.Length > 1 && stored.StartsWith(publicBase, StringComparison.OrdinalIgnoreCase))
            return stored[publicBase.Length..];
        return new Uri(stored).AbsolutePath.TrimStart('/');
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

    /// <summary>
    /// Reads the configured sign-then-pay window (hours) from PlatformSettings, falling back
    /// to 24. Same key the StaleBookingCleanupJob uses, so the contract clause and the
    /// auto-void safeguard stay in lockstep.
    /// </summary>
    private async Task<int> ResolvePaymentConditionHoursAsync(CancellationToken ct)
    {
        var value = await db.PlatformSettings
            .Where(s => s.Key == Jobs.StaleBookingCleanupJob.ExpiryHoursSettingKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

        return value is not null && int.TryParse(value, out var hours) && hours > 0
            ? hours
            : 24;
    }
}
