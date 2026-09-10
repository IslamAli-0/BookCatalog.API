using System.Net;
using System.Net.Http.Json;
using BookCatalog.Core.DTOs;

namespace BookCatalog.IntegrationTests;

/// <summary>
/// Integration tests for the lending flow: borrow, return, loan history, and error cases.
/// </summary>
public class LendingEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    private static readonly Guid TestAuthorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TestUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public LendingEndpointTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<BookResponse> CreateBookAsync()
    {
        var request = new CreateBookRequest
        {
            ISBN = $"978{Random.Shared.NextInt64(1000000000, 9999999999)}",
            Title = $"Lending Test Book {Guid.NewGuid():N}",
            AuthorId = TestAuthorId,
            Genre = "Software",
            PublishYear = 2008
        };

        var response = await _client.PostAsJsonAsync("/api/books", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BookResponse>())!;
    }

    // --- BORROW ---

    [Fact]
    public async Task BorrowBook_WhenAvailable_Returns200AndLoanResponse()
    {
        // Arrange
        var book = await CreateBookAsync();
        var borrowRequest = new BorrowBookRequest { UserId = TestUserId };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/books/{book.Id}/borrow", borrowRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var loan = await response.Content.ReadFromJsonAsync<LoanResponse>();
        Assert.NotNull(loan);
        Assert.Equal(book.Id, loan.BookId);
        Assert.Equal(TestUserId, loan.UserId);
        Assert.Null(loan.ReturnedAt);
    }

    [Fact]
    public async Task BorrowBook_WhenAlreadyBorrowed_Returns409()
    {
        // Arrange — create and borrow
        var book = await CreateBookAsync();
        var borrowRequest = new BorrowBookRequest { UserId = TestUserId };
        await _client.PostAsJsonAsync($"/api/books/{book.Id}/borrow", borrowRequest);

        // Act — try to borrow again
        var response = await _client.PostAsJsonAsync($"/api/books/{book.Id}/borrow", borrowRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task BorrowBook_WhenBookDoesNotExist_Returns404()
    {
        // Arrange
        var borrowRequest = new BorrowBookRequest { UserId = TestUserId };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/books/{Guid.NewGuid()}/borrow", borrowRequest);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BorrowBook_WhenUserDoesNotExist_Returns400()
    {
        // Arrange
        var book = await CreateBookAsync();
        var borrowRequest = new BorrowBookRequest { UserId = Guid.NewGuid() };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/books/{book.Id}/borrow", borrowRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- RETURN ---

    [Fact]
    public async Task ReturnBook_WhenBorrowed_Returns200AndSetsReturnedAt()
    {
        // Arrange — create and borrow
        var book = await CreateBookAsync();
        await _client.PostAsJsonAsync($"/api/books/{book.Id}/borrow",
            new BorrowBookRequest { UserId = TestUserId });

        // Act
        var response = await _client.PostAsJsonAsync($"/api/books/{book.Id}/return", new { });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var loan = await response.Content.ReadFromJsonAsync<LoanResponse>();
        Assert.NotNull(loan);
        Assert.NotNull(loan.ReturnedAt);
    }

    [Fact]
    public async Task ReturnBook_WhenNotBorrowed_Returns409()
    {
        // Arrange — create but do NOT borrow
        var book = await CreateBookAsync();

        // Act
        var response = await _client.PostAsJsonAsync($"/api/books/{book.Id}/return", new { });

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // --- LOAN HISTORY ---

    [Fact]
    public async Task GetLoanHistory_AfterBorrowAndReturn_ContainsLoanRecord()
    {
        // Arrange — borrow and return
        var book = await CreateBookAsync();
        await _client.PostAsJsonAsync($"/api/books/{book.Id}/borrow",
            new BorrowBookRequest { UserId = TestUserId });
        await _client.PostAsJsonAsync($"/api/books/{book.Id}/return", new { });

        // Act
        var response = await _client.GetAsync($"/api/books/{book.Id}/loans");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var loans = await response.Content.ReadFromJsonAsync<List<LoanResponse>>();
        Assert.NotNull(loans);
        Assert.Single(loans);
        Assert.NotNull(loans[0].ReturnedAt);
    }

    // --- FULL FLOW ---

    [Fact]
    public async Task FullLendingFlow_BorrowReturnAndReBorrow_WorksCorrectly()
    {
        // Arrange
        var book = await CreateBookAsync();
        var borrowRequest = new BorrowBookRequest { UserId = TestUserId };

        // Act 1 — Borrow
        var borrowResponse = await _client.PostAsJsonAsync($"/api/books/{book.Id}/borrow", borrowRequest);
        Assert.Equal(HttpStatusCode.OK, borrowResponse.StatusCode);

        // Verify book is unavailable
        var getResponse = await _client.GetAsync($"/api/books/{book.Id}");
        var bookState = await getResponse.Content.ReadFromJsonAsync<BookResponse>();
        Assert.False(bookState!.IsAvailable);

        // Act 2 — Return
        var returnResponse = await _client.PostAsJsonAsync($"/api/books/{book.Id}/return", new { });
        Assert.Equal(HttpStatusCode.OK, returnResponse.StatusCode);

        // Verify book is available again
        getResponse = await _client.GetAsync($"/api/books/{book.Id}");
        bookState = await getResponse.Content.ReadFromJsonAsync<BookResponse>();
        Assert.True(bookState!.IsAvailable);

        // Act 3 — Borrow again (should succeed)
        var reBorrowResponse = await _client.PostAsJsonAsync($"/api/books/{book.Id}/borrow", borrowRequest);
        Assert.Equal(HttpStatusCode.OK, reBorrowResponse.StatusCode);
    }
}
