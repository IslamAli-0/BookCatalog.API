namespace BookCatalog.Core.Models;

public class Loan
{
    public Guid Id { get; set; }
    public Guid BookId { get; set; }
    public virtual Book? Book { get; set; }
    public Guid UserId { get; set; }
    public virtual User? User { get; set; }
    public DateTime BorrowedAt { get; set; } = DateTime.UtcNow;
    public DateTime DueDate { get; set; } = DateTime.UtcNow.AddDays(14);
    public DateTime? ReturnedAt { get; set; }
}
