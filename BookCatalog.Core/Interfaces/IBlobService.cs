namespace BookCatalog.Core.Interfaces;

/// <summary>
/// Abstraction over Azure Blob Storage upload operations and SAS token generation.
/// </summary>
public interface IBlobService
{
    /// <summary>
    /// Streams a file from <paramref name="content"/> directly to the specified blob path and returns
    /// the public URI of the stored blob (Pattern A — server-side proxy upload).
    /// </summary>
    /// <param name="containerName">Target container (e.g., "book-assets").</param>
    /// <param name="blobPath">Relative path inside the container (e.g., "covers/{id}/cover.jpg").</param>
    /// <param name="content">Readable stream containing the file bytes.</param>
    /// <param name="contentType">MIME type, e.g. "image/jpeg" or "application/pdf".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The absolute URI of the newly created blob.</returns>
    Task<Uri> UploadAsync(
        string containerName,
        string blobPath,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a write-only Shared Access Signature URI valid for <paramref name="validForMinutes"/>
    /// minutes so that a client can upload directly to Azure Storage without routing bytes through the API
    /// (Pattern B — direct client upload).
    /// </summary>
    /// <param name="containerName">Target container.</param>
    /// <param name="blobPath">Relative blob path the SAS will be scoped to.</param>
    /// <param name="validForMinutes">How many minutes the SAS token remains valid.</param>
    /// <returns>A pre-signed URI the client can PUT bytes to.</returns>
    Uri GenerateSasUri(string containerName, string blobPath, int validForMinutes = 15);
}
