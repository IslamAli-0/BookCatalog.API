using System.Collections.Concurrent;
using BookCatalog.API.Models;

namespace BookCatalog.API.Repositories;

public class InMemoryBookRepository : IBookRepository
{
    // ConcurrentDictionary is thread-safe for web applications
    private readonly ConcurrentDictionary<Guid, Book> _books = new();

    public Task<IEnumerable<Book>> GetAllAsync()
    {
        return Task.FromResult(_books.Values.AsEnumerable());
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