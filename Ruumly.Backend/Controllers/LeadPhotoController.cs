using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

/// <summary>
/// Photo upload for the public request funnel.
///
/// WHY IT IS ANONYMOUS. The whole point of the concierge front door is that a
/// customer describes their need without an account, and the photos are part of
/// that description — a provider asked for them before they would quote. Putting
/// them behind a login would defeat the funnel; putting them after submission
/// would mean the outreach goes out before the pictures exist.
///
/// WHAT THAT COSTS, AND HOW IT IS PAID. An anonymous endpoint that accepts
/// multi-megabyte bodies is an obvious target — storage exhaustion, and hosting
/// somebody else's content on our bucket. Four defences, none of which trusts
/// the caller:
///
///   • Its own tight rate-limit bucket ("lead-photo"), not the authenticated one.
///   • A hard byte cap checked BEFORE the file is read into memory.
///   • Every upload is decoded and re-encoded (LeadPhotoNormalizer). A file that
///     is not really an image never reaches storage, and the bytes we keep are
///     ones we produced.
///   • The PRIVATE bucket. Objects have no public URL, so nothing here can be
///     linked to from outside — which is what makes "hosting arbitrary content"
///     uninteresting even if a real image gets through.
///
/// Uploads are not tied to a lead: the lead does not exist yet. The client
/// receives opaque keys and submits them with the request. Keys that are never
/// submitted are orphans, and the 30-day purge collects them along with
/// everything else.
/// </summary>
[ApiController]
[Route("api/leads/photos")]
public class LeadPhotoController(
    IStorageService storage,
    ILogger<LeadPhotoController> logger) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("lead-photo")]
    [RequestSizeLimit(LeadPhotoNormalizer.MaxUploadBytes)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file." });

        // Checked before anything reads the stream — the point is to refuse the
        // work, not to do it and then complain.
        if (file.Length > LeadPhotoNormalizer.MaxUploadBytes)
            return BadRequest(new { error = "File is too large." });

        await using var incoming = file.OpenReadStream();
        var normalized = await LeadPhotoNormalizer.NormalizeAsync(incoming, ct);
        if (normalized is null)
            // Deliberately generic: telling an anonymous caller WHY the decoder
            // refused tells them which inputs get furthest.
            return BadRequest(new { error = "That file could not be read as a photo." });

        try
        {
            await using var toStore = new MemoryStream(normalized);
            // Random name, not the customer's — an uploaded filename is
            // attacker-controlled and routinely carries personal detail
            // ("korter-mari-kodu.jpg") we have no reason to keep.
            var key = await storage.UploadPrivateAsync(
                toStore, $"lead-photos/{Guid.NewGuid():N}.jpg", "image/jpeg");

            return Ok(new { key, bytes = normalized.Length });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lead photo upload failed after normalization.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Could not store the photo. Please try again." });
        }
    }
}
