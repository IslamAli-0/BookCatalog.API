using BookCatalog.Core.DTOs;
using BookCatalog.Core.Models;

namespace BookCatalog.Core.Interfaces;

public interface IBookRepository
{
    // Changed to return a Tuple containing the paginated books AND the total count
    Task<(IEnumerable<Book> Books, int TotalCount)> GetAllAsync(BookQueryParameters parameters);
    Task<Book?> GetByIdAsync(Guid id);
    Task<Book> CreateAsync(Book book);
    Task<bool> UpdateAsync(Book book);
    Task<bool> DeleteAsync(Guid id);
}
