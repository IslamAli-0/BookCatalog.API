using System.Text.Json;
using BookCatalog.Core.DTOs;
using BookCatalog.Core.Interfaces;
using BookCatalog.Core.Models;
using BookCatalog.Core.Services;
using BookCatalog.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookCatalog.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BooksController(
    IBookService bookService,
    ApplicationDbContext db,
    IBlobService blobService,
    ILogger<BooksController> logger) : ControllerBase
{
    private const string BookAssetsContainer = "book-assets";

    // ─────────────────────────────────────────────────────────────────────────
    // CRUD
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] BookQueryParameters parameters,
        CancellationToken cancellationToken = default)
    {
        var pagedResult = await bookService.GetAllBooksAsync(parameters);
        return Ok(pagedResult);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var book = await bookService.GetBookByIdAsync(id);

        if (book == null)
        {
            logger.LogWarning("Book with ID {BookId} was not found.", id);
            return NotFound(new { message = $"Book with ID {id} was not found." });
        }

        return Ok(book);
    }

    /// <summary>
    /// Creates a book. Requires an <c>Idempotency-Key</c> header (any unique string, UUID recommended).
    /// A second request with the same key returns the cached 201 response without creating a duplicate.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateBookRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        // ── 1. Require the Idempotency-Key header ──────────────────────────────
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            if (Request.Headers.TryGetValue("Idempotency-Key", out var headerVals))
            {
                idempotencyKey = headerVals.FirstOrDefault();
            }
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(new { message = "The 'Idempotency-Key' header is required for POST /api/books." });
        }

        idempotencyKey = idempotencyKey.Trim();

        // ── 2. Check cache: has this key been processed before? ───────────────
        var existing = await db.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);

        if (existing != null)
        {
            logger.LogInformation("Idempotency cache hit for key {IdempotencyKey}. Replaying cached response.", idempotencyKey);
            var cached = JsonSerializer.Deserialize<BookResponse>(existing.ResponseJson);
            return StatusCode(existing.StatusCode, cached);
        }

        logger.LogInformation("Idempotency cache miss for key {IdempotencyKey}. Creating book with ISBN {ISBN} and title {Title}",
            idempotencyKey, request.ISBN, request.Title);

        // ── 3. Atomic creation: Book + IdempotencyRecord in one transaction ───
        // Use CreateExecutionStrategy() to support user-initiated transactions with EnableRetryOnFailure
        var strategy = db.Database.CreateExecutionStrategy();

        var (bookResult, isNewRecord) = await strategy.ExecuteAsync(async () =>
        {
            var cachedRecord = await db.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);

            if (cachedRecord != null)
            {
                logger.LogInformation("Idempotency cache hit (in transaction) for key {IdempotencyKey}.", idempotencyKey);
                var cached = JsonSerializer.Deserialize<BookResponse>(cachedRecord.ResponseJson);
                return (cached!, false);
            }

            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var createdBook = await bookService.CreateBookAsync(request);

                var record = new IdempotencyRecord
                {
                    Id             = Guid.NewGuid(),
                    IdempotencyKey = idempotencyKey,
                    ResponseJson   = JsonSerializer.Serialize(createdBook),
                    StatusCode     = StatusCodes.Status201Created,
                    CreatedAt      = DateTimeOffset.UtcNow
                };

                db.IdempotencyRecords.Add(record);
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                return (createdBook, true);
            }
            catch (DbUpdateException ex)
                when (ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true ||
                      ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true)
            {
                logger.LogWarning("Idempotency race/conflict detected for key {IdempotencyKey}. Replaying winner response.", idempotencyKey);
                await tx.RollbackAsync(cancellationToken);

                var raceRecord = await db.IdempotencyRecords
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);

                var cached = JsonSerializer.Deserialize<BookResponse>(raceRecord!.ResponseJson);
                return (cached!, false);
            }
        });

        if (isNewRecord)
        {
            logger.LogInformation("Book {BookId} successfully created with ISBN {ISBN}", bookResult.Id, bookResult.ISBN);
            return CreatedAtAction(nameof(GetById), new { id = bookResult.Id }, bookResult);
        }

        return StatusCode(StatusCodes.Status201Created, bookResult);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateBookRequest request,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Updating book {BookId}", id);
        var updatedBook = await bookService.UpdateBookAsync(id, request);

        if (updatedBook == null)
        {
            logger.LogWarning("Failed to update. Book with ID {BookId} was not found.", id);
            return NotFound(new { message = $"Book with ID {id} was not found." });
        }

        logger.LogInformation("Book {BookId} successfully updated", id);
        return Ok(updatedBook);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Deleting book {BookId}", id);
        var deleted = await bookService.DeleteBookAsync(id);

        if (!deleted)
        {
            logger.LogWarning("Failed to delete. Book with ID {BookId} was not found.", id);
            return NotFound(new { message = $"Book with ID {id} was not found." });
        }

        logger.LogInformation("Book {BookId} successfully deleted", id);
        return NoContent(); // 204 No Content
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BLOB STORAGE — Pattern A: Server-side proxy upload
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// (Pattern A) Streams an uploaded file through the API into Azure Blob Storage.
    /// Accepts any file; use <c>assetType=cover</c> (default) or <c>assetType=pdf</c>
    /// to control which URL field on the Book is updated.
    /// </summary>
    [HttpPost("{id:guid}/upload-server")]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB hard limit
    [RequestFormLimits(MultipartBodyLengthLimit = 50 * 1024 * 1024)]
    public async Task<IActionResult> UploadViaServer(
        Guid id,
        IFormFile file,
        [FromQuery] string assetType = "cover",
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "No file was supplied or the file is empty." });
        }

        logger.LogInformation("Uploading server-side asset {AssetType} for book {BookId}", assetType, id);

        // Determine blob path and which Book field to update.
        var extension = Path.GetExtension(file.FileName);
        var blobPath  = assetType.ToLower() switch
        {
            "pdf"   => $"books/{id}/sample{extension}",
            _       => $"books/{id}/cover{extension}"
        };

        await using var stream = file.OpenReadStream();
        var uri = await blobService.UploadAsync(
            BookAssetsContainer,
            blobPath,
            stream,
            file.ContentType,
            cancellationToken);

        // Persist the public URL back onto the Book row.
        var book = await db.Books.FindAsync([id], cancellationToken);
        if (book is null)
        {
            return NotFound(new { message = $"Book with ID {id} was not found." });
        }

        if (assetType.ToLower() == "pdf")
            book.SamplePdfUrl = uri.ToString();
        else
            book.CoverImageUrl = uri.ToString();

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Server asset {AssetType} for book {BookId} uploaded successfully to {BlobPath}", assetType, id, blobPath);

        return Ok(new
        {
            blobUri  = uri.ToString(),
            assetType,
            bookId   = id,
            message  = "File uploaded successfully via server proxy."
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BLOB STORAGE — Pattern B: Direct client upload via SAS token
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// (Pattern B) Issues a write-only SAS URI that the client can use to PUT bytes directly
    /// to Azure Blob Storage — the file bytes never pass through this API.
    /// The token is valid for 15 minutes. Use <c>assetType=cover</c> (default) or
    /// <c>assetType=pdf</c> and <c>fileName</c> for the desired filename.
    /// </summary>
    [HttpPost("{id:guid}/sas-token")]
    public async Task<IActionResult> GetSasToken(
        Guid id,
        [FromQuery] string assetType = "cover",
        [FromQuery] string fileName  = "file",
        CancellationToken cancellationToken = default)
    {
        // Ensure the book exists before issuing a token.
        var bookExists = await db.Books
            .AsNoTracking()
            .AnyAsync(b => b.Id == id, cancellationToken);

        if (!bookExists)
        {
            return NotFound(new { message = $"Book with ID {id} was not found." });
        }

        var extension = Path.GetExtension(fileName);
        var blobPath  = assetType.ToLower() switch
        {
            "pdf"   => $"books/{id}/sample{extension}",
            _       => $"books/{id}/cover{extension}"
        };

        const int validForMinutes = 15;
        logger.LogInformation("Generating SAS upload token for book {BookId}, container {Container}, blob {BlobPath}",
            id, BookAssetsContainer, blobPath);

        var sasUri = blobService.GenerateSasUri(BookAssetsContainer, blobPath, validForMinutes);

        return Ok(new
        {
            sasUri          = sasUri.ToString(),
            blobPath,
            containerName   = BookAssetsContainer,
            expiresInMinutes = validForMinutes,
            instructions    = "HTTP PUT the file bytes directly to 'sasUri'. Set 'x-ms-blob-type: BlockBlob' header."
        });
    }
}