using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

/// <summary>
/// Development-only. Writes files to the local filesystem.
/// In production, CloudflareR2StorageService is registered instead.
/// </summary>
public class LocalDiskStorageService(
    IConfiguration config,
    ILogger<LocalDiskStorageService> logger) : IStorageService
{
    private string BasePath => config["Storage:BasePath"] ?? "/app/uploads";
    private string BaseUrl  => config["Storage:BaseUrl"]  ?? "";

    public async Task<string> UploadAsync(Stream stream, string fileName, string contentType)
    {
        var now = DateTime.UtcNow;
        var dir = Path.Combine(BasePath, now.Year.ToString(), now.Month.ToString("D2"));
        Directory.CreateDirectory(dir);

        var fullPath = Path.Combine(dir, fileName);
        await using var fs = File.Create(fullPath);
        await stream.CopyToAsync(fs);

        var url = $"{BaseUrl}/uploads/{now.Year}/{now.Month:D2}/{fileName}";
        logger.LogInformation("Saved locally: {Path}", fullPath);
        return url;
    }

    public Task DeleteAsync(string publicUrl)
    {
        // Local dev: skip deletion for simplicity
        return Task.CompletedTask;
    }
}
