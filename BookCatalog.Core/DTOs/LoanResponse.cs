namespace BookCatalog.Core.DTOs;

public record LoanResponse
{
    public Guid Id { get; init; }
    public Guid BookId { get; init; }
    public string BookTitle { get; init; } = string.Empty;
    public Guid UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public DateTime BorrowedAt { get; init; }
    public DateTime DueDate { get; init; }
    public DateTime? ReturnedAt { get; init; }
}
