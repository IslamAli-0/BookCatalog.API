using BookCatalog.Core.DTOs;

namespace BookCatalog.Core.Services;

public interface IBookService
{
    Task<PagedResponse<BookResponse>> GetAllBooksAsync(BookQueryParameters parameters);
    Task<BookResponse?> GetBookByIdAsync(Guid id);
    Task<BookResponse> CreateBookAsync(CreateBookRequest request);
    Task<BookResponse?> UpdateBookAsync(Guid id, UpdateBookRequest request);
    Task<bool> DeleteBookAsync(Guid id);
}
