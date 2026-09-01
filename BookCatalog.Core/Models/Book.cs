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
    public virtual ICollection<Loan> LoanHistory { get; set; } = new List<Loan>();

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
