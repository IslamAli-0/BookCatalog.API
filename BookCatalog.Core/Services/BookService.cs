using BookCatalog.Core.DTOs;
using BookCatalog.Core.Interfaces;
using BookCatalog.Core.Mappers;
using BookCatalog.Core.Models;
using Microsoft.Extensions.Logging;

namespace BookCatalog.Core.Services;

public class BookService(IBookRepository repository, ILogger<BookService> logger) : IBookService
{
    public async Task<PagedResponse<BookResponse>> GetAllBooksAsync(BookQueryParameters parameters)
    {
        // Defensive check
        if (parameters.PageNumber < 1) parameters.PageNumber = 1;

        logger.LogInformation("Retrieving books. Page: {Page}, Size: {Size}, Search: {Search}",
            parameters.PageNumber, parameters.PageSize, parameters.SearchTerm ?? "none");

        var (books, totalCount) = await repository.GetAllAsync(parameters);

        return new PagedResponse<BookResponse>
        {
            Items = books.Select(b => b.ToResponse()),
            TotalCount = totalCount,
            PageNumber = parameters.PageNumber,
            PageSize = parameters.PageSize
        };
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
        if (!await repository.AuthorExistsAsync(request.AuthorId))
        {
            throw new ArgumentException($"Author with ID {request.AuthorId} does not exist.");
        }

        var newBook = request.ToBook();

        var createdBook = await repository.CreateAsync(newBook);

        logger.LogInformation("Successfully created book {Title} with ID {BookId}", createdBook.Title, createdBook.Id);

        return createdBook.ToResponse();
    }

    public async Task<BookResponse?> UpdateBookAsync(Guid id, UpdateBookRequest request)
    {
        if (!await repository.AuthorExistsAsync(request.AuthorId))
        {
            throw new ArgumentException($"Author with ID {request.AuthorId} does not exist.");
        }

        var existingBook = await repository.GetByIdAsync(id);

        if (existingBook == null)
        {
            logger.LogWarning("Failed to update. Book with ID {BookId} was not found.", id);
            return null;
        }

        existingBook.ApplyUpdate(request);

        await repository.UpdateAsync(existingBook);

        logger.LogInformation("Successfully updated book ID {BookId}", existingBook.Id);

        var refreshedBook = await repository.GetByIdAsync(id);
        return refreshedBook!.ToResponse();
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
