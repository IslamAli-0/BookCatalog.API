using BookCatalog.Core.DTOs;

namespace BookCatalog.Core.Services;

public interface IBookService
{
    Task<IEnumerable<BookResponse>> GetAllBooksAsync();
    Task<BookResponse?> GetBookByIdAsync(Guid id);
    Task<BookResponse> CreateBookAsync(CreateBookRequest request);
    Task<BookResponse?> UpdateBookAsync(Guid id, UpdateBookRequest request);
    Task<bool> DeleteBookAsync(Guid id);
}
