namespace BookCatalog.Core.DTOs;

public record BookResponse
{
    public Guid Id { get; init; }
    public string ISBN { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string AuthorName { get; init; } = string.Empty;
    public string Genre { get; init; } = string.Empty;
    public int PublishYear { get; init; }
    public string? Description { get; init; }
    public bool IsAvailable { get; init; }
}
