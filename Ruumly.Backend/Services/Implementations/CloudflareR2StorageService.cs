using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class CloudflareR2StorageService(
    IConfiguration config,
    ILogger<CloudflareR2StorageService> logger) : IStorageService
{
    private AmazonS3Client CreateClient()
    {
        var accountId = config["Storage:R2AccountId"]!;
        var accessKey = config["Storage:R2AccessKey"]!;
        var secretKey = config["Storage:R2SecretKey"]!;
        var endpoint  = $"https://{accountId}.r2.cloudflarestorage.com";

        return new AmazonS3Client(
            new BasicAWSCredentials(accessKey, secretKey),
            new AmazonS3Config
            {
                ServiceURL           = endpoint,
                ForcePathStyle       = true,   // required for R2
                AuthenticationRegion = "auto",  // R2 uses 'auto'
            });
    }

    public async Task<string> UploadAsync(Stream stream, string fileName, string contentType)
    {
        var bucket    = config["Storage:R2BucketName"]!;
        var publicUrl = config["Storage:R2PublicUrl"]!;

        var now = DateTime.UtcNow;
        var key = $"{now.Year}/{now.Month:D2}/{fileName}";

        using var client = CreateClient();

        var request = new PutObjectRequest
        {
            BucketName  = bucket,
            Key         = key,
            InputStream = stream,
            ContentType = contentType,
            CannedACL   = S3CannedACL.PublicRead,
        };

        await client.PutObjectAsync(request);

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

        using var client = CreateClient();
        await client.DeleteObjectAsync(bucket, key);
        logger.LogInformation("Deleted from R2: {Key}", key);
    }
}
