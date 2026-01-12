using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace GenPosting.Api.Services;

public class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobContainerClient _containerClient;

    public AzureBlobStorageService(IConfiguration configuration)
    {
        var connectionString = configuration["Storage:ConnectionString"];
        var containerName = configuration["Storage:ContainerName"];

        if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(containerName))
        {
            throw new ArgumentNullException("Storage configuration is missing.");
        }

        var blobServiceClient = new BlobServiceClient(connectionString);
        _containerClient = blobServiceClient.GetBlobContainerClient(containerName);
    }

    public async Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType)
    {
        await _containerClient.CreateIfNotExistsAsync();
        
        // We do NOT need to set public access if we use SAS tokens.
        // This makes it work even if "Allow Blob public access" is disabled on the account level.

        // Use purely random name to avoid any special character issues in URLs
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension)) 
        {
            extension = contentType.Contains("video") ? ".mp4" : ".jpg";
        }

        var uniqueFileName = $"{Guid.NewGuid()}{extension}";
        var blobClient = _containerClient.GetBlobClient(uniqueFileName);

        var blobUploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        };

        await blobClient.UploadAsync(fileStream, blobUploadOptions);

        // Generate a SAS token valid for 1 hour (enough for Instagram to download it)
        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _containerClient.Name,
            BlobName = uniqueFileName,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.AddHours(1)
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        return blobClient.GenerateSasUri(sasBuilder).ToString();
    }
}
