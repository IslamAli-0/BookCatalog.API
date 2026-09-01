namespace BookCatalog.Core.Models;

public class Author
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Biography { get; set; }
    public virtual ICollection<Book> Books { get; set; } = new List<Book>();
}
