using BookCatalog.Core.DTOs;

namespace BookCatalog.Core.Services;

public interface ILendingService
{
    Task<LoanResponse> BorrowBookAsync(Guid bookId, BorrowBookRequest request);
    Task<LoanResponse> ReturnBookAsync(Guid bookId);
    Task<IEnumerable<LoanResponse>> GetBookLoanHistoryAsync(Guid bookId);
}
