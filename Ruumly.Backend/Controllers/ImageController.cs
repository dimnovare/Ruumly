using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Services.Interfaces;

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

    [HttpPost("upload")]
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

        var urls = new List<string>();

        foreach (var file in files)
        {
            var ext      = Path.GetExtension(file.FileName).ToLowerInvariant();
            var fileName = $"{Guid.NewGuid()}{ext}";

            await using var stream = file.OpenReadStream();
            var url = await storage.UploadAsync(stream, fileName, file.ContentType);
            urls.Add(url);
            logger.LogInformation("Image uploaded: {Url}", url);
        }

        return Ok(urls);
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
