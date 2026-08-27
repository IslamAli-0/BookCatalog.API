namespace BookCatalog.Core.DTOs;

public class BookQueryParameters
{
    const int maxPageSize = 50;
    public int PageNumber { get; set; } = 1;

    private int _pageSize = 10;
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = (value > maxPageSize) ? maxPageSize : value;
    }

    // Filters
    public string? Genre { get; set; }
    public string? SearchTerm { get; set; } // Will search against both Title and Author
}
