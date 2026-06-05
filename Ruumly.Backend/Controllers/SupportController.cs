using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api")]
public class SupportController(
    RuumlyDbContext db,
    IEmailSender emailSender,
    ILogger<SupportController> logger) : ControllerBase
{
    /// <summary>
    /// Public contact form. Emails the team the visitor's message.
    /// The send is fire-and-forget so a transient email failure never
    /// breaks the visitor's submit (mirrors the notify-interest pattern).
    /// </summary>
    [HttpPost("contact")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Contact([FromBody] ContactRequest req)
    {
        // Resolve the team inbox from PlatformSettings; fall back to the
        // public contact address used elsewhere in the app.
        var teamEmail = await db.PlatformSettings
            .Where(s => s.Key == "siteEmail")
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(teamEmail))
            teamEmail = "info@ruumly.eu";

        var lang = string.IsNullOrWhiteSpace(req.Language) ? "et" : req.Language;

        emailSender.SendAsync(
            to:       teamEmail,
            subject:  $"[Ruumly contact] {req.Subject}",
            textBody: $"From: {req.Name} <{req.Email}>\nLang: {lang}\n\n{req.Message}\n\n— Reply directly to {req.Email}")
            .FireAndForget(logger, "contact-form");

        return Ok(new { success = true });
    }
}
