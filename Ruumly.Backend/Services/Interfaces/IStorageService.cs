namespace Ruumly.Backend.Services.Interfaces;

public interface IStorageService
{
    /// <summary>
    /// Upload a stream and return the full public URL of the stored file.
    /// </summary>
    Task<string> UploadAsync(Stream stream, string fileName, string contentType);

    /// <summary>
    /// Delete a file by its full public URL. No-op if the file does not exist.
    /// </summary>
    Task DeleteAsync(string publicUrl);
}
