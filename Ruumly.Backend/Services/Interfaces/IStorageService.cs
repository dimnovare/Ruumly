namespace Ruumly.Backend.Services.Interfaces;

public interface IStorageService
{
    /// <summary>
    /// Upload a stream and return the full public URL of the stored file.
    /// </summary>
    Task<string> UploadAsync(Stream stream, string fileName, string contentType);

    /// <summary>
    /// Upload a stream and return both the storage object key (stable, for read-back)
    /// and the full public URL. Use this when the bytes must be retrieved later
    /// (e.g. docx templates that are filled per booking).
    /// </summary>
    Task<StoredObject> UploadWithKeyAsync(Stream stream, string fileName, string contentType);

    /// <summary>
    /// Download the bytes of a previously stored object by its storage key
    /// (the <see cref="StoredObject.Key"/> from <see cref="UploadWithKeyAsync"/>).
    /// Returns null when the object does not exist.
    /// </summary>
    Task<byte[]?> DownloadAsync(string key);

    /// <summary>
    /// Delete a file by its full public URL. No-op if the file does not exist.
    /// </summary>
    Task DeleteAsync(string publicUrl);
}

/// <summary>A stored object: its stable storage key and its public URL.</summary>
public record StoredObject(string Key, string Url);
