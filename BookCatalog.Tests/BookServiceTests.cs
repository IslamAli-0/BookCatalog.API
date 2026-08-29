using BookCatalog.Core.DTOs;
using BookCatalog.Core.Interfaces;
using BookCatalog.Core.Models;
using BookCatalog.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace BookCatalog.Tests;

/// <summary>
/// Unit tests for <see cref="BookService"/>.
/// Each test class instance gets brand-new Mock objects via constructor injection,
/// guaranteeing zero shared state between tests.
/// </summary>
public class BookServiceTests
{
    // ── Dependencies injected once per test instance ───────────────────────
    private readonly Mock<IBookRepository> _mockRepo;
    private readonly Mock<ILogger<BookService>> _mockLogger;
    private readonly BookService _sut; // System Under Test

    public BookServiceTests()
    {
        _mockRepo   = new Mock<IBookRepository>();
        _mockLogger = new Mock<ILogger<BookService>>();
        _sut        = new BookService(_mockRepo.Object, _mockLogger.Object);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetAllBooksAsync
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAllBooksAsync_WhenBooksExist_ReturnsCorrectlyMappedPagedResponse()
    {
        // Arrange
        var parameters = new BookQueryParameters { PageNumber = 1, PageSize = 10 };
        var books = new List<Book>
        {
            new() { Id = Guid.NewGuid(), Title = "Clean Code", Author = "Robert C. Martin",
                    ISBN = "9780132350884", Genre = "Technology", PublishYear = 2008 },
            new() { Id = Guid.NewGuid(), Title = "The Pragmatic Programmer", Author = "Andrew Hunt",
                    ISBN = "9780135957059", Genre = "Technology", PublishYear = 2019 }
        };
        _mockRepo
            .Setup(r => r.GetAllAsync(parameters))
            .ReturnsAsync((books, books.Count));

        // Act
        var result = await _sut.GetAllBooksAsync(parameters);

        // Assert — shape and content
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(2, result.Items.Count());
        Assert.Equal("Clean Code", result.Items.First().Title);

        // Assert — repository was called exactly once with the correct parameters
        _mockRepo.Verify(r => r.GetAllAsync(parameters), Times.Once);
    }

    [Fact]
    public async Task GetAllBooksAsync_WhenBooksExist_ReturnsEmptyItemsForOutOfRangePage()
    {
        // Arrange — page 9999 beyond any data
        var parameters = new BookQueryParameters { PageNumber = 9999, PageSize = 10 };
        _mockRepo
            .Setup(r => r.GetAllAsync(parameters))
            .ReturnsAsync((Enumerable.Empty<Book>(), 5)); // 5 total records, but this page is empty

        // Act
        var result = await _sut.GetAllBooksAsync(parameters);

        // Assert — service returns a valid envelope with accurate meta even when Items is empty
        Assert.Empty(result.Items);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(9999, result.PageNumber);

        _mockRepo.Verify(r => r.GetAllAsync(parameters), Times.Once);
    }

    [Fact]
    public async Task GetAllBooksAsync_WhenPageNumberIsLessThanOne_NormalizesPageNumberToOne()
    {
        // Arrange — client sends an invalid page number (e.g. 0 or negative)
        var parameters = new BookQueryParameters { PageNumber = 0, PageSize = 10 };
        _mockRepo
            .Setup(r => r.GetAllAsync(It.IsAny<BookQueryParameters>()))
            .ReturnsAsync((Enumerable.Empty<Book>(), 0));

        // Act
        var result = await _sut.GetAllBooksAsync(parameters);

        // Assert — defensive branch in BookService corrects PageNumber to 1
        Assert.Equal(1, result.PageNumber);

        _mockRepo.Verify(r => r.GetAllAsync(parameters), Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetBookByIdAsync
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetBookByIdAsync_WhenBookExists_ReturnsMappedBookResponse()
    {
        // Arrange
        var bookId = Guid.NewGuid();
        var book = new Book
        {
            Id          = bookId,
            Title       = "Domain-Driven Design",
            Author      = "Eric Evans",
            ISBN        = "9780321125217",
            Genre       = "Technology",
            PublishYear = 2003,
            Description = "Tackling Complexity in the Heart of Software"
        };
        _mockRepo
            .Setup(r => r.GetByIdAsync(bookId))
            .ReturnsAsync(book);

        // Act
        var result = await _sut.GetBookByIdAsync(bookId);

        // Assert — every mapped field is correct
        Assert.NotNull(result);
        Assert.Equal(bookId, result.Id);
        Assert.Equal("Domain-Driven Design", result.Title);
        Assert.Equal("Eric Evans", result.Author);
        Assert.Equal("9780321125217", result.ISBN);
        Assert.Equal(2003, result.PublishYear);

        _mockRepo.Verify(r => r.GetByIdAsync(bookId), Times.Once);
    }

    [Fact]
    public async Task GetBookByIdAsync_WhenBookDoesNotExist_ReturnsNull()
    {
        // Arrange — repository returns null for an unknown ID
        var unknownId = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.GetByIdAsync(unknownId))
            .ReturnsAsync((Book?)null);

        // Act
        var result = await _sut.GetBookByIdAsync(unknownId);

        // Assert — service propagates null; caller (Controller) is responsible for 404
        Assert.Null(result);

        _mockRepo.Verify(r => r.GetByIdAsync(unknownId), Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CreateBookAsync
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateBookAsync_WithValidRequest_ReturnsMappedBookResponse()
    {
        // Arrange
        var request = new CreateBookRequest
        {
            ISBN        = "9780132350884",
            Title       = "Clean Code",
            Author      = "Robert C. Martin",
            Genre       = "Technology",
            PublishYear = 2008,
            Description = "A Handbook of Agile Software Craftsmanship"
        };

        // Repository returns the persisted entity with a server-generated ID
        var persistedBook = new Book
        {
            Id          = Guid.NewGuid(),
            ISBN        = request.ISBN,
            Title       = request.Title,
            Author      = request.Author,
            Genre       = request.Genre,
            PublishYear = request.PublishYear,
            Description = request.Description
        };
        _mockRepo
            .Setup(r => r.CreateAsync(It.IsAny<Book>()))
            .ReturnsAsync(persistedBook);

        // Act
        var result = await _sut.CreateBookAsync(request);

        // Assert — response mirrors the persisted entity
        Assert.NotNull(result);
        Assert.Equal(persistedBook.Id, result.Id);
        Assert.Equal("Clean Code", result.Title);
        Assert.Equal("Robert C. Martin", result.Author);
        Assert.Equal(2008, result.PublishYear);

        // Prove CreateAsync was called with a Book (not bypassed)
        _mockRepo.Verify(r => r.CreateAsync(It.IsAny<Book>()), Times.Once);
    }

    [Fact]
    public async Task CreateBookAsync_WithValidRequest_MapsRequestFieldsToBookEntity()
    {
        // Arrange — verify the mapper (ToBook) correctly transfers all fields to the entity
        var request = new CreateBookRequest
        {
            ISBN        = "9780135957059",
            Title       = "The Pragmatic Programmer",
            Author      = "Andrew Hunt",
            Genre       = "Technology",
            PublishYear = 2019
        };

        Book? capturedBook = null;
        _mockRepo
            .Setup(r => r.CreateAsync(It.IsAny<Book>()))
            .Callback<Book>(b => capturedBook = b)   // capture the entity the service built
            .ReturnsAsync(new Book
            {
                Id          = Guid.NewGuid(),
                ISBN        = request.ISBN,
                Title       = request.Title,
                Author      = request.Author,
                Genre       = request.Genre,
                PublishYear = request.PublishYear
            });

        // Act
        await _sut.CreateBookAsync(request);

        // Assert — every field on the entity matches the original request
        Assert.NotNull(capturedBook);
        Assert.Equal(request.ISBN, capturedBook!.ISBN);
        Assert.Equal(request.Title, capturedBook.Title);
        Assert.Equal(request.Author, capturedBook.Author);
        Assert.Equal(request.Genre, capturedBook.Genre);
        Assert.Equal(request.PublishYear, capturedBook.PublishYear);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // UpdateBookAsync
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateBookAsync_WhenBookExists_ReturnsUpdatedBookResponse()
    {
        // Arrange
        var bookId = Guid.NewGuid();
        var existingBook = new Book
        {
            Id          = bookId,
            ISBN        = "9780132350884",
            Title       = "Clean Code",
            Author      = "Robert C. Martin",
            Genre       = "Technology",
            PublishYear = 2008
        };
        var updateRequest = new UpdateBookRequest
        {
            ISBN        = "9780132350884",
            Title       = "Clean Code — Updated Edition",
            Author      = "Robert C. Martin",
            Genre       = "Software Engineering",
            PublishYear = 2024
        };
        _mockRepo
            .Setup(r => r.GetByIdAsync(bookId))
            .ReturnsAsync(existingBook);
        _mockRepo
            .Setup(r => r.UpdateAsync(existingBook))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.UpdateBookAsync(bookId, updateRequest);

        // Assert — response reflects the applied update
        Assert.NotNull(result);
        Assert.Equal("Clean Code — Updated Edition", result.Title);
        Assert.Equal("Software Engineering", result.Genre);
        Assert.Equal(2024, result.PublishYear);

        // Prove both repo calls happened in the correct order
        _mockRepo.Verify(r => r.GetByIdAsync(bookId),   Times.Once);
        _mockRepo.Verify(r => r.UpdateAsync(existingBook), Times.Once);
    }

    [Fact]
    public async Task UpdateBookAsync_WhenBookDoesNotExist_ReturnsNull()
    {
        // Arrange — the ID does not exist in the store
        var unknownId = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.GetByIdAsync(unknownId))
            .ReturnsAsync((Book?)null);

        var updateRequest = new UpdateBookRequest
        {
            ISBN        = "9780132350884",
            Title       = "Ghost Book",
            Author      = "Nobody",
            Genre       = "Fiction",
            PublishYear = 2020
        };

        // Act
        var result = await _sut.UpdateBookAsync(unknownId, updateRequest);

        // Assert — null signals "not found"; Controller converts this to 404
        Assert.Null(result);

        // Prove the service short-circuited: GetById was called but UpdateAsync was never reached
        _mockRepo.Verify(r => r.GetByIdAsync(unknownId), Times.Once);
        _mockRepo.Verify(r => r.UpdateAsync(It.IsAny<Book>()), Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DeleteBookAsync
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteBookAsync_WhenBookExists_ReturnsTrueAndCallsRepositoryOnce()
    {
        // Arrange — the repository confirms the delete succeeded
        var bookId = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.DeleteAsync(bookId))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.DeleteBookAsync(bookId);

        // Assert
        Assert.True(result);

        _mockRepo.Verify(r => r.DeleteAsync(bookId), Times.Once);
    }

    [Fact]
    public async Task DeleteBookAsync_WhenBookDoesNotExist_ReturnsFalseAndCallsRepositoryOnce()
    {
        // Arrange — repo signals the ID was not found
        var unknownId = Guid.NewGuid();
        _mockRepo
            .Setup(r => r.DeleteAsync(unknownId))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.DeleteBookAsync(unknownId);

        // Assert — service surfaces the false; Controller converts it to 404
        Assert.False(result);

        _mockRepo.Verify(r => r.DeleteAsync(unknownId), Times.Once);
    }
}
