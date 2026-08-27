namespace BookCatalog.Core.DTOs;

public class PagedResponse<T>
{
    public IEnumerable<T> Items { get; set; } = Enumerable.Empty<T>();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    
    // Calculated property to help the client build UI pagination buttons
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
