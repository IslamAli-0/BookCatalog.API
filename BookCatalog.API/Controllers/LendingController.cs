using BookCatalog.Core.DTOs;
using BookCatalog.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookCatalog.API.Controllers;

[ApiController]
[Route("api/books")]
public class LendingController(ILendingService lendingService, ILogger<LendingController> logger) : ControllerBase
{
    [HttpPost("{id:guid}/borrow")]
    public async Task<ActionResult<LoanResponse>> BorrowBook(
        Guid id,
        [FromBody] BorrowBookRequest request,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Borrowing book {BookId} for user {UserId}", id, request.UserId);
        var loan = await lendingService.BorrowBookAsync(id, request);
        logger.LogInformation("Book {BookId} successfully borrowed. LoanId={LoanId}", id, loan.Id);
        return Ok(loan);
    }

    [HttpPost("{id:guid}/return")]
    public async Task<ActionResult<LoanResponse>> ReturnBook(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Returning book {BookId}", id);
        var loan = await lendingService.ReturnBookAsync(id);
        logger.LogInformation("Book {BookId} successfully returned. LoanId={LoanId}", id, loan.Id);
        return Ok(loan);
    }

    [HttpGet("{id:guid}/loans")]
    public async Task<ActionResult<IEnumerable<LoanResponse>>> GetLoanHistory(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Retrieving loan history for book {BookId}", id);
        var history = await lendingService.GetBookLoanHistoryAsync(id);
        return Ok(history);
    }
}
