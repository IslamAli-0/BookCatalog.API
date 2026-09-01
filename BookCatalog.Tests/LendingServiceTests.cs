using BookCatalog.Core.DTOs;
using BookCatalog.Core.Interfaces;
using BookCatalog.Core.Models;
using BookCatalog.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BookCatalog.Tests;

public class LendingServiceTests
{
    private readonly Mock<ILendingRepository> _mockRepo;
    private readonly Mock<ILogger<LendingService>> _mockLogger;
    private readonly LendingService _sut;

    public LendingServiceTests()
    {
        _mockRepo = new Mock<ILendingRepository>();
        _mockLogger = new Mock<ILogger<LendingService>>();
        _sut = new LendingService(_mockRepo.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task BorrowBookAsync_WhenUserDoesNotExist_ThrowsArgumentException()
    {
        // Arrange
        var bookId = Guid.NewGuid();
        var request = new BorrowBookRequest { UserId = Guid.NewGuid() };
        _mockRepo.Setup(r => r.UserExistsAsync(request.UserId)).ReturnsAsync(false);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => _sut.BorrowBookAsync(bookId, request));
        Assert.Contains("does not exist", exception.Message);
    }

    [Fact]
    public async Task BorrowBookAsync_WhenBookIsAvailable_ReturnsLoanResponse()
    {
        // Arrange
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var request = new BorrowBookRequest { UserId = userId };
        
        var loan = new Loan
        {
            Id = Guid.NewGuid(),
            BookId = bookId,
            UserId = userId,
            BorrowedAt = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(14),
            Book = new Book { Title = "Test Book" },
            User = new User { FullName = "Test User" }
        };

        _mockRepo.Setup(r => r.UserExistsAsync(userId)).ReturnsAsync(true);
        _mockRepo.Setup(r => r.BorrowBookAsync(bookId, userId)).ReturnsAsync(loan);

        // Act
        var result = await _sut.BorrowBookAsync(bookId, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(loan.Id, result.Id);
        Assert.Equal(loan.BookId, result.BookId);
        Assert.Equal("Test Book", result.BookTitle);
        Assert.Equal("Test User", result.UserName);
    }

    [Fact]
    public async Task ReturnBookAsync_WhenSuccessful_ReturnsLoanResponse()
    {
        // Arrange
        var bookId = Guid.NewGuid();
        var loan = new Loan
        {
            Id = Guid.NewGuid(),
            BookId = bookId,
            UserId = Guid.NewGuid(),
            BorrowedAt = DateTime.UtcNow.AddDays(-5),
            DueDate = DateTime.UtcNow.AddDays(9),
            ReturnedAt = DateTime.UtcNow,
            Book = new Book { Title = "Test Book" },
            User = new User { FullName = "Test User" }
        };

        _mockRepo.Setup(r => r.ReturnBookAsync(bookId)).ReturnsAsync(loan);

        // Act
        var result = await _sut.ReturnBookAsync(bookId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(loan.Id, result.Id);
        Assert.NotNull(result.ReturnedAt);
        Assert.Equal("Test Book", result.BookTitle);
    }

    [Fact]
    public async Task GetBookLoanHistoryAsync_ReturnsMappedHistory()
    {
        // Arrange
        var bookId = Guid.NewGuid();
        var loans = new List<Loan>
        {
            new Loan { Id = Guid.NewGuid(), BookId = bookId, UserId = Guid.NewGuid() },
            new Loan { Id = Guid.NewGuid(), BookId = bookId, UserId = Guid.NewGuid() }
        };

        _mockRepo.Setup(r => r.GetBookLoanHistoryAsync(bookId)).ReturnsAsync(loans);

        // Act
        var result = await _sut.GetBookLoanHistoryAsync(bookId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count());
    }
}
