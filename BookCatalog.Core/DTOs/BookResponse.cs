namespace BookCatalog.Core.DTOs;

public record BookResponse
{
    public Guid Id { get; init; }
    public string ISBN { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string Genre { get; init; } = string.Empty;
    public int PublishYear { get; init; }
    public string? Description { get; init; }
}
