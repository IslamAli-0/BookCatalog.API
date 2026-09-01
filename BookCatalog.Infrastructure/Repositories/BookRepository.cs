using BookCatalog.Core.DTOs;
using BookCatalog.Core.Interfaces;
using BookCatalog.Core.Models;
using BookCatalog.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BookCatalog.Infrastructure.Repositories;

public class BookRepository(ApplicationDbContext context) : IBookRepository
{
    public async Task<(IEnumerable<Book> Books, int TotalCount)> GetAllAsync(BookQueryParameters parameters)
    {
        var query = context.Books
            .Include(b => b.Author)
            .AsNoTracking()
            .AsQueryable();

        // 1. Apply Filters
        if (!string.IsNullOrWhiteSpace(parameters.Genre))
        {
            query = query.Where(b => b.Genre.ToLower() == parameters.Genre.ToLower());
        }

        if (!string.IsNullOrWhiteSpace(parameters.SearchTerm))
        {
            var term = parameters.SearchTerm.ToLower();
            query = query.Where(b =>
                b.Title.ToLower().Contains(term) ||
                b.Author!.Name.ToLower().Contains(term));
        }

        // 2. Get the Total Count (AFTER filtering, BEFORE paginating)
        var totalCount = await query.CountAsync();

        // 3. Apply Pagination (Skip and Take)
        var pagedBooks = await query
            .OrderBy(b => b.Title)
            .Skip((parameters.PageNumber - 1) * parameters.PageSize)
            .Take(parameters.PageSize)
            .ToListAsync();

        return (pagedBooks, totalCount);
    }

    public async Task<Book?> GetByIdAsync(Guid id)
    {
        return await context.Books
            .Include(b => b.Author)
            .FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<Book> CreateAsync(Book book)
    {
        book.Id = Guid.NewGuid();
        context.Books.Add(book);
        await context.SaveChangesAsync();

        // Reload with Author navigation populated for the response mapping
        await context.Entry(book).Reference(b => b.Author).LoadAsync();

        return book;
    }

    public async Task<bool> UpdateAsync(Book book)
    {
        context.Books.Update(book);
        var rowsAffected = await context.SaveChangesAsync();
        return rowsAffected > 0;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var book = await context.Books.FindAsync(id);
        if (book is null)
        {
            return false;
        }

        context.Books.Remove(book);
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> AuthorExistsAsync(Guid authorId)
    {
        return await context.Authors.AnyAsync(a => a.Id == authorId);
    }
}
