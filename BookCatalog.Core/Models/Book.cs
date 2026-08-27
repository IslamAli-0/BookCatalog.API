namespace BookCatalog.Core.Models;

public class Book
{
    public Guid Id { get; set; }
    public string ISBN { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public int PublishYear { get; set; }
    public string? Description { get; set; } 
}
