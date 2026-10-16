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
    IBlobService blobService) : ControllerBase
{
    private const string BookAssetsContainer = "book-assets";

    // ─────────────────────────────────────────────────────────────────────────
    // CRUD
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] BookQueryParameters parameters)
    {
        var pagedResult = await bookService.GetAllBooksAsync(parameters);
        return Ok(pagedResult);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var book = await bookService.GetBookByIdAsync(id);

        if (book == null)
        {
            return NotFound(new { message = $"Book with ID {id} was not found." });
        }

        return Ok(book);
    }

    /// <summary>
    /// Creates a book. Requires an <c>Idempotency-Key</c> header (any unique string, UUID recommended).
    /// A second request with the same key returns the cached 201 response without creating a duplicate.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBookRequest request)
    {
        // ── 1. Require the Idempotency-Key header ──────────────────────────────
        if (!Request.Headers.TryGetValue("Idempotency-Key", out var keyValues) ||
            string.IsNullOrWhiteSpace(keyValues.FirstOrDefault()))
        {
            return BadRequest(new { message = "The 'Idempotency-Key' header is required for POST /api/books." });
        }

        var idempotencyKey = keyValues.First()!.Trim();

        // ── 2. Check cache: has this key been processed before? ───────────────
        var existing = await db.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey);

        if (existing != null)
        {
            // Replay the original response byte-for-byte.
            var cached = JsonSerializer.Deserialize<BookResponse>(existing.ResponseJson);
            return StatusCode(existing.StatusCode, cached);
        }

        // ── 3. Atomic creation: Book + IdempotencyRecord in one transaction ───
        // Use CreateExecutionStrategy() to support user-initiated transactions with EnableRetryOnFailure
        var strategy = db.Database.CreateExecutionStrategy();

        var (bookResult, isNewRecord) = await strategy.ExecuteAsync(async () =>
        {
            var cachedRecord = await db.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey);

            if (cachedRecord != null)
            {
                var cached = JsonSerializer.Deserialize<BookResponse>(cachedRecord.ResponseJson);
                return (cached!, false);
            }

            await using var tx = await db.Database.BeginTransactionAsync();
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
                await db.SaveChangesAsync();
                await tx.CommitAsync();

                return (createdBook, true);
            }
            catch (DbUpdateException ex)
                when (ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true ||
                      ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true)
            {
                await tx.RollbackAsync();

                var raceRecord = await db.IdempotencyRecords
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey);

                var cached = JsonSerializer.Deserialize<BookResponse>(raceRecord!.ResponseJson);
                return (cached!, false);
            }
        });

        if (isNewRecord)
        {
            return CreatedAtAction(nameof(GetById), new { id = bookResult.Id }, bookResult);
        }

        return StatusCode(StatusCodes.Status201Created, bookResult);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBookRequest request)
    {
        var updatedBook = await bookService.UpdateBookAsync(id, request);

        if (updatedBook == null)
        {
            return NotFound(new { message = $"Book with ID {id} was not found." });
        }

        return Ok(updatedBook);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await bookService.DeleteBookAsync(id);

        if (!deleted)
        {
            return NotFound(new { message = $"Book with ID {id} was not found." });
        }

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