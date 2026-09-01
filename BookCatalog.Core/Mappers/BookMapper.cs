using BookCatalog.Core.DTOs;
using BookCatalog.Core.Models;

namespace BookCatalog.Core.Mappers;

public static class BookMapper
{
    public static BookResponse ToResponse(this Book book)
    {
        return new BookResponse
        {
            Id = book.Id,
            ISBN = book.ISBN,
            Title = book.Title,
            AuthorName = book.Author?.Name ?? "Unknown",
            Genre = book.Genre,
            PublishYear = book.PublishYear,
            Description = book.Description,
            IsAvailable = book.IsAvailable
        };
    }

    public static Book ToBook(this CreateBookRequest request)
    {
        return new Book
        {
            ISBN = request.ISBN,
            Title = request.Title,
            AuthorId = request.AuthorId,
            Genre = request.Genre,
            PublishYear = request.PublishYear,
            Description = request.Description
        };
    }

    public static void ApplyUpdate(this Book book, UpdateBookRequest request)
    {
        book.ISBN = request.ISBN;
        book.Title = request.Title;
        book.AuthorId = request.AuthorId;
        book.Genre = request.Genre;
        book.PublishYear = request.PublishYear;
        book.Description = request.Description;
    }
}
