using BookCatalog.Core.Models;

namespace BookCatalog.Core.Repositories;

public interface IBookRepository
{
    Task<IEnumerable<Book>> GetAllAsync();
    Task<Book?> GetByIdAsync(Guid id);
    Task<Book> CreateAsync(Book book);
    Task<bool> UpdateAsync(Book book);
    Task<bool> DeleteAsync(Guid id);
}
