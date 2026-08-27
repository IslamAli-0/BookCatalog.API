using System.Collections.Concurrent;
using BookCatalog.Core.DTOs;
using BookCatalog.Core.Interfaces;
using BookCatalog.Core.Models;

namespace BookCatalog.Infrastructure.Repositories;

public class InMemoryBookRepository : IBookRepository
{
    // ConcurrentDictionary is thread-safe for web applications
    private readonly ConcurrentDictionary<Guid, Book> _books = new();

    public Task<(IEnumerable<Book> Books, int TotalCount)> GetAllAsync(BookQueryParameters parameters)
    {
        var query = _books.Values.AsEnumerable();

        // 1. Apply Filters
        if (!string.IsNullOrWhiteSpace(parameters.Genre))
        {
            query = query.Where(b => b.Genre.Equals(parameters.Genre, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(parameters.SearchTerm))
        {
            var term = parameters.SearchTerm.ToLower();
            query = query.Where(b => b.Title.ToLower().Contains(term) || b.Author.ToLower().Contains(term));
        }

        // 2. Get the Total Count (AFTER filtering, BEFORE paginating)
        var totalCount = query.Count();

        // 3. Apply Pagination (Skip and Take)
        var pagedBooks = query
            .Skip((parameters.PageNumber - 1) * parameters.PageSize)
            .Take(parameters.PageSize)
            .ToList();

        return Task.FromResult((pagedBooks.AsEnumerable(), totalCount));
    }

    public Task<Book?> GetByIdAsync(Guid id)
    {
        _books.TryGetValue(id, out var book);
        return Task.FromResult(book);
    }

    public Task<Book> CreateAsync(Book book)
    {
        book.Id = Guid.NewGuid();
        _books.TryAdd(book.Id, book);

        return Task.FromResult(book);
    }

    public Task<bool> UpdateAsync(Book book)
    {
        if (!_books.ContainsKey(book.Id))
        {
            return Task.FromResult(false);
        }

        _books[book.Id] = book;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(Guid id)
    {
        var removed = _books.TryRemove(id, out _);
        return Task.FromResult(removed);
    }
}
