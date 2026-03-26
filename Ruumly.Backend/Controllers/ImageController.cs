using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ruumly.Backend.Services.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/images")]
[Authorize(Roles = "Admin,Provider")]
public class ImageController(
    IStorageService storage,
    IConfiguration config,
    ILogger<ImageController> logger) : ControllerBase
{
    private static readonly string[] AllowedContentTypes =
        ["image/jpeg", "image/jpg", "image/png", "image/webp"];

    private const long MaxFileSizeBytes   = 5 * 1024 * 1024; // 5 MB
    private const int  MaxFilesPerRequest = 10;
    private const int  MaxFullWidth       = 1200;
    private const int  MaxThumbWidth      = 400;
    private const int  WebpQuality        = 80;

    [HttpPost("upload")]
    [EnableRateLimiting("upload")]
    public async Task<IActionResult> Upload([FromForm] IFormFileCollection files)
    {
        if (files.Count == 0)
            return BadRequest(new { error = "No files provided" });

        if (files.Count > MaxFilesPerRequest)
            return BadRequest(new { error = $"Maximum {MaxFilesPerRequest} files per request" });

        foreach (var file in files)
        {
            if (file.Length > MaxFileSizeBytes)
                return BadRequest(new { error = $"File '{file.FileName}' exceeds 5 MB limit" });

            if (!AllowedContentTypes.Contains(file.ContentType.ToLowerInvariant()))
                return BadRequest(new { error = $"Content type '{file.ContentType}' is not allowed" });
        }

        var results = new List<object>();
        var encoder = new WebpEncoder { Quality = WebpQuality };

        foreach (var file in files)
        {
            var baseName = Guid.NewGuid().ToString();
            var fullName  = $"{baseName}.webp";
            var thumbName = $"thumb_{baseName}.webp";

            Image image;
            try
            {
                await using var input = file.OpenReadStream();
                image = await Image.LoadAsync(input);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to decode image '{FileName}'", file.FileName);
                return BadRequest(new { error = $"File '{file.FileName}' is not a valid image." });
            }

            using (image)
            using (var thumb = image.Clone(_ => {}))
            {
                // ── full-size (resize original) ────────────────────────────
                if (image.Width > MaxFullWidth)
                    image.Mutate(x => x.Resize(MaxFullWidth, 0));

                var fullStream = new MemoryStream();
                await image.SaveAsync(fullStream, encoder);
                fullStream.Position = 0;
                var fullUrl = await storage.UploadAsync(fullStream, fullName, "image/webp");

                // ── thumbnail (resize clone independently) ─────────────────
                if (thumb.Width > MaxThumbWidth)
                    thumb.Mutate(x => x.Resize(MaxThumbWidth, 0));

                var thumbStream = new MemoryStream();
                await thumb.SaveAsync(thumbStream, encoder);
                thumbStream.Position = 0;
                var thumbUrl = await storage.UploadAsync(thumbStream, thumbName, "image/webp");

                logger.LogInformation("Uploaded image full={Full} thumb={Thumb}", fullUrl, thumbUrl);
                results.Add(new { full = fullUrl, thumb = thumbUrl });
            }
        }

        return Ok(results);
    }

    // Fallback serving for local dev only.
    // In production, images are served directly by Cloudflare CDN.
    [HttpGet("{**filePath}")]
    [AllowAnonymous]
    public IActionResult Get(string filePath)
    {
        if (filePath.Contains("..")) return BadRequest();

        var basePath = config["Storage:BasePath"] ?? "/app/uploads";

        // Canonicalize path to prevent traversal
        var fullPath = Path.GetFullPath(
            Path.Combine(basePath, filePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!fullPath.StartsWith(Path.GetFullPath(basePath)))
            return BadRequest();

        if (!System.IO.File.Exists(fullPath)) return NotFound();

        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        var contentType = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png"            => "image/png",
            ".webp"           => "image/webp",
            _                 => "application/octet-stream",
        };

        return PhysicalFile(fullPath, contentType);
    }
}
