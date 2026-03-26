using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class CloudflareR2StorageService(
    IConfiguration config,
    ILogger<CloudflareR2StorageService> logger) : IStorageService, IDisposable
{
    private readonly AmazonS3Client _s3 = new(
        new BasicAWSCredentials(
            config["Storage:R2AccessKey"]!,
            config["Storage:R2SecretKey"]!),
        new AmazonS3Config
        {
            ServiceURL           = $"https://{config["Storage:R2AccountId"]!}.r2.cloudflarestorage.com",
            ForcePathStyle       = true,   // required for R2
            AuthenticationRegion = "auto",  // R2 uses 'auto'
        });

    public async Task<string> UploadAsync(Stream stream, string fileName, string contentType)
    {
        var bucket    = config["Storage:R2BucketName"]!;
        var publicUrl = config["Storage:R2PublicUrl"]!;

        var now = DateTime.UtcNow;
        var key = $"{now.Year}/{now.Month:D2}/{fileName}";

        var request = new PutObjectRequest
        {
            BucketName  = bucket,
            Key         = key,
            InputStream = stream,
            ContentType = contentType,
            CannedACL   = S3CannedACL.PublicRead,
        };

        await _s3.PutObjectAsync(request);

        var url = $"{publicUrl}/{key}";
        logger.LogInformation("Uploaded to R2: {Url}", url);
        return url;
    }

    public async Task DeleteAsync(string publicUrl)
    {
        var bucket   = config["Storage:R2BucketName"]!;
        var r2Public = config["Storage:R2PublicUrl"]!;

        if (!publicUrl.StartsWith(r2Public)) return;

        var key = publicUrl[r2Public.Length..].TrimStart('/');

        await _s3.DeleteObjectAsync(bucket, key);
        logger.LogInformation("Deleted from R2: {Key}", key);
    }

    public void Dispose() => _s3.Dispose();
}
