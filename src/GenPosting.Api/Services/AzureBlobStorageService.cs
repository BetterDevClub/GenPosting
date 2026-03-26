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

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension))
            extension = contentType.Contains("video") ? ".mp4" : ".jpg";

        var blobName = $"{Guid.NewGuid()}{extension}";
        var blobClient = _containerClient.GetBlobClient(blobName);

        var blobUploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        };

        await blobClient.UploadAsync(fileStream, blobUploadOptions);

        return blobName;
    }

    public Task<string> GetSasUrlAsync(string blobName, TimeSpan expiry)
    {
        var blobClient = _containerClient.GetBlobClient(blobName);
        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _containerClient.Name,
            BlobName = blobName,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.Add(expiry)
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);
        return Task.FromResult(blobClient.GenerateSasUri(sasBuilder).ToString());
    }
}
