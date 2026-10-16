using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using BookCatalog.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace BookCatalog.Infrastructure.Services;

/// <summary>
/// Azure Blob Storage implementation of <see cref="IBlobService"/>.
/// Supports server-side proxy uploads (Pattern A) and SAS-token generation for direct client
/// uploads (Pattern B).
/// </summary>
public class BlobService(BlobServiceClient blobServiceClient, ILogger<BlobService> logger) : IBlobService
{
    /// <inheritdoc />
    public async Task<Uri> UploadAsync(
        string containerName,
        string blobPath,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

        // Ensure the container exists (idempotent, does nothing if already present)
        await containerClient.CreateIfNotExistsAsync(
            PublicAccessType.None,      // No anonymous read — all access via SAS or MI
            cancellationToken: cancellationToken);

        var blobClient = containerClient.GetBlobClient(blobPath);

        logger.LogInformation("Uploading blob {BlobPath} to container {Container}", blobPath, containerName);

        await blobClient.UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            },
            cancellationToken);

        logger.LogInformation("Blob {BlobPath} uploaded successfully. URI: {Uri}", blobPath, blobClient.Uri);

        return blobClient.Uri;
    }

    /// <inheritdoc />
    public Uri GenerateSasUri(string containerName, string blobPath, int validForMinutes = 15)
    {
        var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobPath);

        // Verify that the service client was initialised with credentials that can sign SAS tokens.
        // Connection-string clients (StorageSharedKeyCredential) and User-Delegation flows both
        // support this; anonymous / token-only flows do not.
        if (!blobClient.CanGenerateSasUri)
        {
            throw new InvalidOperationException(
                "The BlobServiceClient was not initialised with a StorageSharedKeyCredential. " +
                "SAS token generation requires a connection string or shared-key credential. " +
                "Ensure AzureBlobStorage__ConnectionString is set in configuration.");
        }

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = containerName,
            BlobName          = blobPath,
            Resource          = "b",   // "b" = blob (not container)
            ExpiresOn         = DateTimeOffset.UtcNow.AddMinutes(validForMinutes)
        };

        // Write-only: the client can PUT bytes but cannot read or delete.
        sasBuilder.SetPermissions(BlobSasPermissions.Write | BlobSasPermissions.Create);

        var sasUri = blobClient.GenerateSasUri(sasBuilder);

        logger.LogInformation(
            "Generated SAS URI for blob {BlobPath} in container {Container}. Expires in {Minutes} minutes.",
            blobPath, containerName, validForMinutes);

        return sasUri;
    }
}
