using BookCatalog.Core.DTOs;
using BookCatalog.Core.Mappers;
using BookCatalog.Core.Models;
using BookCatalog.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace BookCatalog.Core.Services;

public class BookService(IBookRepository repository, ILogger<BookService> logger) : IBookService
{
    public async Task<IEnumerable<BookResponse>> GetAllBooksAsync()
    {
        logger.LogInformation("Retrieving all books from the catalog.");
        var books = await repository.GetAllAsync();

        return books.Select(b => b.ToResponse());
    }

    public async Task<BookResponse?> GetBookByIdAsync(Guid id)
    {
        var book = await repository.GetByIdAsync(id);

        if (book == null)
        {
            logger.LogWarning("Book with ID {BookId} was not found.", id);
            return null;
        }

        return book.ToResponse();
    }

    public async Task<BookResponse> CreateBookAsync(CreateBookRequest request)
    {
        var newBook = request.ToBook();

        var createdBook = await repository.CreateAsync(newBook);

        logger.LogInformation("Successfully created book {Title} with ID {BookId}", createdBook.Title, createdBook.Id);

        return createdBook.ToResponse();
    }

    public async Task<BookResponse?> UpdateBookAsync(Guid id, UpdateBookRequest request)
    {
        var existingBook = await repository.GetByIdAsync(id);

        if (existingBook == null)
        {
            logger.LogWarning("Failed to update. Book with ID {BookId} was not found.", id);
            return null;
        }

        existingBook.ApplyUpdate(request);

        await repository.UpdateAsync(existingBook);

        logger.LogInformation("Successfully updated book ID {BookId}", existingBook.Id);

        return existingBook.ToResponse();
    }

    public async Task<bool> DeleteBookAsync(Guid id)
    {
        var deleted = await repository.DeleteAsync(id);

        if (deleted)
        {
            logger.LogInformation("Successfully deleted book ID {BookId}", id);
        }
        else
        {
            logger.LogWarning("Failed to delete. Book with ID {BookId} was not found.", id);
        }

        return deleted;
    }
}
