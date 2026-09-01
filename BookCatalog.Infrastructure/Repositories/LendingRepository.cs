using BookCatalog.Core.Interfaces;
using BookCatalog.Core.Models;
using BookCatalog.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BookCatalog.Infrastructure.Repositories;

public class LendingRepository(ApplicationDbContext context) : ILendingRepository
{
    public async Task<bool> UserExistsAsync(Guid userId)
    {
        return await context.Users.AnyAsync(u => u.Id == userId);
    }

    public async Task<Loan> BorrowBookAsync(Guid bookId, Guid userId)
    {
        // Using an explicit transaction to satisfy Week 3 assignment requirements ("Choose one such operation in your domain and make it safe").
        await using var transaction = await context.Database.BeginTransactionAsync();

        try
        {
            var book = await context.Books.FirstOrDefaultAsync(b => b.Id == bookId);

            if (book == null)
            {
                throw new BookCatalog.Core.Exceptions.NotFoundException($"Book with ID {bookId} was not found.");
            }

            if (!book.IsAvailable)
            {
                throw new BookCatalog.Core.Exceptions.ConflictException("Book is already borrowed.");
            }

            book.IsAvailable = false;

            var now = DateTime.UtcNow;
            var loan = new Loan
            {
                BookId = bookId,
                UserId = userId,
                BorrowedAt = now,
                DueDate = now.AddDays(14)
            };

            context.Loans.Add(loan);

            // Since Book has a [Timestamp] RowVersion column, if another transaction successfully updated IsAvailable
            // at the exact same time, this SaveChangesAsync will throw a DbUpdateConcurrencyException,
            // keeping our data perfectly safe from the race condition.
            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            // Load navigations for mapping
            await context.Entry(loan).Reference(l => l.Book).LoadAsync();
            await context.Entry(loan).Reference(l => l.User).LoadAsync();

            return loan;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<Loan> ReturnBookAsync(Guid bookId)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();

        try
        {
            var book = await context.Books.FirstOrDefaultAsync(b => b.Id == bookId);

            if (book == null)
            {
                throw new BookCatalog.Core.Exceptions.NotFoundException($"Book with ID {bookId} was not found.");
            }

            if (book.IsAvailable)
            {
                throw new BookCatalog.Core.Exceptions.ConflictException("Book is not currently borrowed.");
            }

            var activeLoan = await context.Loans
                .FirstOrDefaultAsync(l => l.BookId == bookId && l.ReturnedAt == null);

            if (activeLoan == null)
            {
                throw new BookCatalog.Core.Exceptions.NotFoundException("Active loan record not found for this book.");
            }

            book.IsAvailable = true;
            activeLoan.ReturnedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            await context.Entry(activeLoan).Reference(l => l.Book).LoadAsync();
            await context.Entry(activeLoan).Reference(l => l.User).LoadAsync();

            return activeLoan;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<IEnumerable<Loan>> GetBookLoanHistoryAsync(Guid bookId)
    {
        return await context.Loans
            .AsNoTracking()
            .Include(l => l.Book)
            .Include(l => l.User)
            .Where(l => l.BookId == bookId)
            .OrderByDescending(l => l.BorrowedAt)
            .ToListAsync();
    }
}
