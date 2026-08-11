using BookCatalog.API.DTOs;
using BookCatalog.API.Models;

namespace BookCatalog.API.Mappers;

public static class BookMapper
{
    public static BookResponse ToResponse(this Book book)
    {
        return new BookResponse
        {
            Id = book.Id,
            ISBN = book.ISBN,
            Title = book.Title,
            Author = book.Author,
            Genre = book.Genre,
            PublishYear = book.PublishYear,
            Description = book.Description
        };
    }

    public static Book ToBook(this CreateBookRequest request)
    {
        return new Book
        {
            ISBN = request.ISBN,
            Title = request.Title,
            Author = request.Author,
            Genre = request.Genre,
            PublishYear = request.PublishYear,
            Description = request.Description
        };
    }

    public static void ApplyUpdate(this Book book, UpdateBookRequest request)
    {
        book.ISBN = request.ISBN;
        book.Title = request.Title;
        book.Author = request.Author;
        book.Genre = request.Genre;
        book.PublishYear = request.PublishYear;
        book.Description = request.Description;
    }
}