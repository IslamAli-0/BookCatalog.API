namespace BookCatalog.Core.Models;

/// <summary>
/// Stores the result of a previously completed mutating request so that replaying the same
/// Idempotency-Key header returns the identical HTTP response without executing the operation twice.
/// </summary>
public class IdempotencyRecord
{
    public Guid Id { get; set; }

    /// <summary>Client-supplied opaque key (UUID recommended). Unique per operation.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Serialised JSON body of the original HTTP response (e.g., the BookResponse JSON).</summary>
    public string ResponseJson { get; set; } = string.Empty;

    /// <summary>HTTP status code returned for the original request (e.g., 201).</summary>
    public int StatusCode { get; set; }

    /// <summary>UTC timestamp when the record was first persisted.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
