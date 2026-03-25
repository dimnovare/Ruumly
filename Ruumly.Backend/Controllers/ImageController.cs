using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/images")]
[Authorize(Roles = "Admin,Provider")]
public class ImageController(
    IConfiguration config,
    ILogger<ImageController> logger) : ControllerBase
{
    private string BasePath => config["Storage:BasePath"] ?? "/app/uploads";
    private string BaseUrl  => config["Storage:BaseUrl"]  ?? "";

    private static readonly string[] AllowedExtensions =
        [".jpg", ".jpeg", ".png", ".webp"];

    private static readonly string[] AllowedContentTypes =
        ["image/jpeg", "image/jpg", "image/png", "image/webp"];

    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB
    private const int  MaxFilesPerRequest = 10;

    /// <summary>
    /// Upload one or more images. Returns an array of relative URLs.
    /// </summary>
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

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext))
                return BadRequest(new { error = $"File type '{ext}' is not allowed. Use jpg, jpeg, png, or webp." });

            if (!AllowedContentTypes.Contains(file.ContentType.ToLowerInvariant()))
                return BadRequest(new { error = $"Content type '{file.ContentType}' is not allowed." });
        }

        var urls = new List<string>();
        var now  = DateTime.UtcNow;
        var dir  = Path.Combine(BasePath, now.Year.ToString(), now.Month.ToString("D2"));
        Directory.CreateDirectory(dir);

        foreach (var file in files)
        {
            var ext      = Path.GetExtension(file.FileName).ToLowerInvariant();
            var filename = $"{Guid.NewGuid()}{ext}";
            var fullPath = Path.Combine(dir, filename);

            await using var stream = System.IO.File.Create(fullPath);
            await file.CopyToAsync(stream);

            logger.LogInformation("Uploaded image: {Path}", fullPath);

            var url = $"{BaseUrl}/uploads/{now.Year}/{now.Month:D2}/{filename}";
            urls.Add(url);
        }

        return Ok(urls);
    }

    /// <summary>
    /// Serve an uploaded file by its relative path (year/month/filename).
    /// Static file middleware at /uploads handles this in production;
    /// this endpoint is a fallback for environments where that isn't configured.
    /// </summary>
    [HttpGet("{**filePath}")]
    [AllowAnonymous]
    public IActionResult Get(string filePath)
    {
        // Prevent path traversal
        if (filePath.Contains(".."))
            return BadRequest();

        var fullPath = Path.Combine(BasePath, filePath.Replace('/', Path.DirectorySeparatorChar));

        if (!System.IO.File.Exists(fullPath))
            return NotFound();

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
