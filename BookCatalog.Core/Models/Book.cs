using System.ComponentModel.DataAnnotations;

namespace BookCatalog.Core.Models;

public class Book
{
    public Guid Id { get; set; }
    public string ISBN { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public Guid AuthorId { get; set; }
    public virtual Author? Author { get; set; }
    public string Genre { get; set; } = string.Empty;
    public int PublishYear { get; set; }
    public string? Description { get; set; }
    public bool IsAvailable { get; set; } = true;

    /// <summary>URL of the book cover image stored in Azure Blob Storage.</summary>
    public string? CoverImageUrl { get; set; }

    /// <summary>URL of the sample PDF stored in Azure Blob Storage.</summary>
    public string? SamplePdfUrl { get; set; }
    public virtual ICollection<Loan> LoanHistory { get; set; } = new List<Loan>();

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
