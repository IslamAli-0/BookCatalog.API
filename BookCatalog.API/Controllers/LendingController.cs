using BookCatalog.Core.DTOs;
using BookCatalog.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookCatalog.API.Controllers;

[ApiController]
[Route("api/books")]
public class LendingController(ILendingService lendingService) : ControllerBase
{
    [HttpPost("{id:guid}/borrow")]
    public async Task<ActionResult<LoanResponse>> BorrowBook(Guid id, [FromBody] BorrowBookRequest request)
    {
        var loan = await lendingService.BorrowBookAsync(id, request);
        return Ok(loan);
    }

    [HttpPost("{id:guid}/return")]
    public async Task<ActionResult<LoanResponse>> ReturnBook(Guid id)
    {
        var loan = await lendingService.ReturnBookAsync(id);
        return Ok(loan);
    }

    [HttpGet("{id:guid}/loans")]
    public async Task<ActionResult<IEnumerable<LoanResponse>>> GetLoanHistory(Guid id)
    {
        var history = await lendingService.GetBookLoanHistoryAsync(id);
        return Ok(history);
    }
}
