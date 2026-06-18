using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Services.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Ruumly.Backend.Controllers;

/// <summary>
/// Self-service account endpoints for the authenticated user.
/// Lives under the same /api/auth prefix as AuthController so the SPA's
/// existing auth axios instance (which attaches the bearer token) reaches it.
/// </summary>
[ApiController]
[Route("api/auth/account")]
[Authorize]
public class AccountController(
    RuumlyDbContext db,
    IStorageService storage,
    IAuthService authService,
    ILogger<AccountController> logger) : ControllerBase
{
    private static readonly string[] AllowedContentTypes =
        ["image/jpeg", "image/jpg", "image/png", "image/webp"];

    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB
    private const int  AvatarSize       = 256;             // square avatar edge, px
    private const int  WebpQuality      = 82;

    /// <summary>
    /// Upload a new avatar for the authenticated user. Accepts a single multipart
    /// image (jpeg/png/webp, ≤5 MB), re-encodes it to a square WebP, stores it via the
    /// shared storage service, sets User.Avatar to the returned URL, and best-effort
    /// deletes the previous avatar. Returns the new url and the refreshed user profile.
    /// </summary>
    [HttpPost("avatar")]
    [EnableRateLimiting("upload")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UploadAvatar(IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file provided." });

        if (file.Length > MaxFileSizeBytes)
            return BadRequest(new { error = "Avatar exceeds the 5 MB limit." });

        if (!AllowedContentTypes.Contains(file.ContentType.ToLowerInvariant()))
            return BadRequest(new { error = $"Content type '{file.ContentType}' is not allowed." });

        var userId = User.GetUserId();
        var user   = await db.Users.FindAsync(userId);
        if (user is null) return NotFound(new { error = "User not found." });

        Image image;
        try
        {
            await using var input = file.OpenReadStream();
            image = await Image.LoadAsync(input);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to decode avatar for user {UserId}", userId);
            return BadRequest(new { error = "File is not a valid image." });
        }

        string newUrl;
        using (image)
        {
            // Square crop to centre, then downscale to a fixed avatar edge.
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(AvatarSize, AvatarSize),
                Mode = ResizeMode.Crop,
            }));

            var encoder = new WebpEncoder { Quality = WebpQuality };
            var output  = new MemoryStream();
            await image.SaveAsync(output, encoder);
            output.Position = 0;

            var fileName = $"avatar_{userId}_{Guid.NewGuid():N}.webp";
            newUrl = await storage.UploadAsync(output, fileName, "image/webp");
        }

        var previousAvatar = user.Avatar;
        user.Avatar = newUrl;

        db.AuditLogs.Add(new AuditLog
        {
            Id        = Guid.NewGuid(),
            Action    = "account.avatar_updated",
            Actor     = user.Email,
            Target    = userId.ToString(),
            Detail    = "User uploaded a new avatar.",
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();

        // Best-effort cleanup of the old avatar — never fail the request on this.
        if (!string.IsNullOrEmpty(previousAvatar) && previousAvatar != newUrl)
        {
            try { await storage.DeleteAsync(previousAvatar); }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to delete previous avatar '{Url}' for user {UserId}",
                    previousAvatar, userId);
            }
        }

        var profile = await authService.GetMeAsync(userId);
        return Ok(new { url = newUrl, user = profile });
    }
}
