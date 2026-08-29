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
    public async Task GetAllBooksAsync_WithItemsSpanningMultiplePages_CalculatesTotalPagesCorrectly()
    {
        // Arrange — 10 total items with page size of 3 should yield 4 pages (ceiling of 10/3)
        var parameters = new BookQueryParameters { PageNumber = 1, PageSize = 3 };
        var books = Enumerable.Range(1, 3).Select(i => new Book
        {
            Id          = Guid.NewGuid(),
            Title       = $"Book {i}",
            Author      = "Author",
            ISBN        = "9780132350884",
            Genre       = "Technology",
            PublishYear = 2020
        });
        _mockRepo
            .Setup(r => r.GetAllAsync(parameters))
            .ReturnsAsync((books, 10)); // 10 total, but this page only holds 3

        // Act
        var result = await _sut.GetAllBooksAsync(parameters);

        // Assert — TotalPages must round up, not truncate
        Assert.Equal(10, result.TotalCount);
        Assert.Equal(4, result.TotalPages); // Math.Ceiling(10 / 3.0) = 4

        _mockRepo.Verify(r => r.GetAllAsync(parameters), Times.Once);
    }

    [Fact]
    public async Task GetAllBooksAsync_WithFilterParameters_PassesParametersToRepositoryUnchanged()
    {
        // Arrange — the service must not silently drop Genre or SearchTerm
        var parameters = new BookQueryParameters
        {
            PageNumber = 1,
            PageSize   = 10,
            Genre      = "Technology",
            SearchTerm = "Clean"
        };
        _mockRepo
            .Setup(r => r.GetAllAsync(parameters))
            .ReturnsAsync((Enumerable.Empty<Book>(), 0));

        // Act
        await _sut.GetAllBooksAsync(parameters);

        // Assert — the exact parameters object (with Genre and SearchTerm intact) reached the repo
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

        // Capture the PageNumber value at the exact moment the repo is called.
        // This proves normalization happened BEFORE the repository was invoked, not after.
        // A plain Verify(parameters) would pass even if the repo was called with PageNumber=0
        // and the parameter was mutated to 1 afterwards, because both point to the same object.
        int? pageNumberAtRepoCall = null;
        _mockRepo
            .Setup(r => r.GetAllAsync(It.IsAny<BookQueryParameters>()))
            .Callback<BookQueryParameters>(p => pageNumberAtRepoCall = p.PageNumber)
            .ReturnsAsync((Enumerable.Empty<Book>(), 0));

        // Act
        var result = await _sut.GetAllBooksAsync(parameters);

        // Assert — both the returned value AND the value seen by the repo are 1
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(1, pageNumberAtRepoCall);

        _mockRepo.Verify(r => r.GetAllAsync(It.IsAny<BookQueryParameters>()), Times.Once);
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
        Assert.Equal("Tackling Complexity in the Heart of Software", result.Description);

        _mockRepo.Verify(r => r.GetByIdAsync(bookId), Times.Once);
    }

    [Fact]
    public async Task GetBookByIdAsync_WhenBookHasNoDescription_ReturnsMappedResponseWithNullDescription()
    {
        // Arrange — Description is optional on the domain model; the response must preserve null
        var bookId = Guid.NewGuid();
        var book = new Book
        {
            Id          = bookId,
            Title       = "No Description Book",
            Author      = "Anonymous",
            ISBN        = "9780132350884",
            Genre       = "Technology",
            PublishYear = 2010,
            Description = null
        };
        _mockRepo
            .Setup(r => r.GetByIdAsync(bookId))
            .ReturnsAsync(book);

        // Act
        var result = await _sut.GetBookByIdAsync(bookId);

        // Assert — null must be mapped as-is, not converted to empty string or omitted
        Assert.NotNull(result);
        Assert.Null(result.Description);

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

        // The service must NOT pre-assign an ID — that responsibility belongs to the repository
        Assert.Equal(Guid.Empty, capturedBook.Id);
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

        // Prove both repo calls were made (Verify does not enforce ordering;
        // the test name and AAA structure communicate the expected sequence)
        _mockRepo.Verify(r => r.GetByIdAsync(bookId),      Times.Once);
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
