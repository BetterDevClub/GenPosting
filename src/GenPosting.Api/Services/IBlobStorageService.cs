namespace GenPosting.Api.Services;

public interface IBlobStorageService
{
    /// <summary>Uploads a file and returns the blob name (not a SAS URL).</summary>
    Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType);

    /// <summary>Generates a fresh SAS URL for an already-uploaded blob.</summary>
    Task<string> GetSasUrlAsync(string blobName, TimeSpan expiry);
}
