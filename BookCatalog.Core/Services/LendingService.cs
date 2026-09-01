using BookCatalog.Core.DTOs;
using BookCatalog.Core.Interfaces;
using BookCatalog.Core.Mappers;
using Microsoft.Extensions.Logging;

namespace BookCatalog.Core.Services;

public class LendingService(ILendingRepository repository, ILogger<LendingService> logger) : ILendingService
{
    public async Task<LoanResponse> BorrowBookAsync(Guid bookId, BorrowBookRequest request)
    {
        if (!await repository.UserExistsAsync(request.UserId))
        {
            throw new ArgumentException($"User with ID {request.UserId} does not exist.");
        }

        logger.LogInformation("User {UserId} is attempting to borrow book {BookId}", request.UserId, bookId);

        var loan = await repository.BorrowBookAsync(bookId, request.UserId);

        logger.LogInformation("Successfully borrowed book {BookId} for user {UserId}", bookId, request.UserId);

        return loan.ToResponse();
    }

    public async Task<LoanResponse> ReturnBookAsync(Guid bookId)
    {
        logger.LogInformation("Attempting to return book {BookId}", bookId);

        var loan = await repository.ReturnBookAsync(bookId);

        logger.LogInformation("Successfully returned book {BookId}", bookId);

        return loan.ToResponse();
    }

    public async Task<IEnumerable<LoanResponse>> GetBookLoanHistoryAsync(Guid bookId)
    {
        var history = await repository.GetBookLoanHistoryAsync(bookId);

        return history.Select(l => l.ToResponse());
    }
}
