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
        => (await UploadWithKeyAsync(stream, fileName, contentType)).Url;

    public async Task<StoredObject> UploadWithKeyAsync(Stream stream, string fileName, string contentType)
    {
        var now = DateTime.UtcNow;
        var key = $"{now.Year}/{now.Month:D2}/{fileName}";
        var dir = Path.Combine(BasePath, now.Year.ToString(), now.Month.ToString("D2"));
        Directory.CreateDirectory(dir);

        var fullPath = Path.Combine(dir, fileName);
        await using var fs = File.Create(fullPath);
        await stream.CopyToAsync(fs);

        var url = $"{BaseUrl}/uploads/{key}";
        logger.LogInformation("Saved locally: {Path}", fullPath);
        return new StoredObject(key, url);
    }

    public Task<byte[]?> DownloadAsync(string key)
    {
        var fullPath = Path.Combine(BasePath, key.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(fullPath)
            ? Task.FromResult<byte[]?>(File.ReadAllBytes(fullPath))
            : Task.FromResult<byte[]?>(null);
    }

    public Task DeleteAsync(string publicUrl)
    {
        // Local dev: skip deletion for simplicity
        return Task.CompletedTask;
    }

    // Local dev has no separate private bucket — store under a "private/" subtree
    // of the same BasePath so signed contracts are at least path-segregated on disk.
    public async Task<string> UploadPrivateAsync(Stream stream, string fileName, string contentType)
    {
        var now = DateTime.UtcNow;
        var key = $"private/{now.Year}/{now.Month:D2}/{fileName}";
        var dir = Path.Combine(BasePath, "private", now.Year.ToString(), now.Month.ToString("D2"));
        Directory.CreateDirectory(dir);

        var fullPath = Path.Combine(dir, fileName);
        await using var fs = File.Create(fullPath);
        await stream.CopyToAsync(fs);

        logger.LogInformation("Saved privately (local): {Path}", fullPath);
        return key;
    }

    public Task<byte[]?> DownloadPrivateAsync(string key)
    {
        var fullPath = Path.Combine(BasePath, key.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(fullPath)
            ? Task.FromResult<byte[]?>(File.ReadAllBytes(fullPath))
            : Task.FromResult<byte[]?>(null);
    }
}
