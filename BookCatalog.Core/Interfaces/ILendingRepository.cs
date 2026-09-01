using BookCatalog.Core.Models;

namespace BookCatalog.Core.Interfaces;

public interface ILendingRepository
{
    Task<Loan> BorrowBookAsync(Guid bookId, Guid userId);
    Task<Loan> ReturnBookAsync(Guid bookId);
    Task<IEnumerable<Loan>> GetBookLoanHistoryAsync(Guid bookId);
    Task<bool> UserExistsAsync(Guid userId);
}
