using BookCatalog.Core.DTOs;
using BookCatalog.Core.Models;

namespace BookCatalog.Core.Mappers;

public static class LoanMapper
{
    public static LoanResponse ToResponse(this Loan loan)
    {
        return new LoanResponse
        {
            Id = loan.Id,
            BookId = loan.BookId,
            BookTitle = loan.Book?.Title ?? "Unknown",
            UserId = loan.UserId,
            UserName = loan.User?.FullName ?? "Unknown",
            BorrowedAt = loan.BorrowedAt,
            DueDate = loan.DueDate,
            ReturnedAt = loan.ReturnedAt
        };
    }
}
