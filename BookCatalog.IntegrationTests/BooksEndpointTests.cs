using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookCatalog.Core.DTOs;

namespace BookCatalog.IntegrationTests;

/// <summary>
/// Integration tests for Book CRUD, Idempotency, and Blob Storage operations.
/// Tests the full HTTP pipeline against a real SQL Server container.
/// </summary>
public class BooksEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    // Seeded in Program.cs when ASPNETCORE_ENVIRONMENT=Development
    private static readonly Guid TestAuthorId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public BooksEndpointTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private CreateBookRequest CreateValidRequest(string? title = null) => new()
    {
        ISBN = $"978{Random.Shared.NextInt64(1000000000, 9999999999)}",
        Title = title ?? $"Test Book {Guid.NewGuid():N}",
        AuthorId = TestAuthorId,
        Genre = "Software",
        PublishYear = 2008,
        Description = "A test book for integration testing."
    };

    private async Task<HttpResponseMessage> PostBookAsync(CreateBookRequest request, string? idempotencyKey = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/books")
        {
            Content = JsonContent.Create(request)
        };
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            message.Headers.Add("Idempotency-Key", idempotencyKey);
        }
        return await _client.SendAsync(message);
    }

    // --- CREATE & IDEMPOTENCY ---

    [Fact]
    public async Task CreateBook_WithValidData_Returns201AndBookResponse()
    {
        // Arrange
        var request = CreateValidRequest();
        var key = Guid.NewGuid().ToString();

        // Act
        var response = await PostBookAsync(request, key);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var book = await response.Content.ReadFromJsonAsync<BookResponse>();
        Assert.NotNull(book);
        Assert.NotEqual(Guid.Empty, book.Id);
        Assert.Equal(request.Title, book.Title);
        Assert.Equal(request.ISBN, book.ISBN);
        Assert.Equal(request.Genre, book.Genre);
        Assert.Equal(request.PublishYear, book.PublishYear);
        Assert.True(book.IsAvailable);

        // Location header must point to the new resource
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task CreateBook_WithoutIdempotencyKey_Returns400()
    {
        // Arrange
        var request = CreateValidRequest();

        // Act - no Idempotency-Key header provided
        var response = await PostBookAsync(request, idempotencyKey: null);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateBook_WithSameIdempotencyKey_ReturnsCachedResponseAndDoesNotDuplicate()
    {
        // Arrange
        var request = CreateValidRequest();
        var key = Guid.NewGuid().ToString();

        // Act 1 — initial creation
        var response1 = await PostBookAsync(request, key);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);
        var book1 = await response1.Content.ReadFromJsonAsync<BookResponse>();

        // Act 2 — replay exact same request with same idempotency key
        var response2 = await PostBookAsync(request, key);
        Assert.Equal(HttpStatusCode.Created, response2.StatusCode);
        var book2 = await response2.Content.ReadFromJsonAsync<BookResponse>();

        // Assert — replayed response is byte-for-byte / identical entity ID
        Assert.NotNull(book1);
        Assert.NotNull(book2);
        Assert.Equal(book1.Id, book2.Id);
        Assert.Equal(book1.ISBN, book2.ISBN);
    }

    [Fact]
    public async Task CreateBook_WithMissingTitle_Returns400()
    {
        // Arrange
        var request = new CreateBookRequest
        {
            ISBN = $"978{Random.Shared.NextInt64(1000000000, 9999999999)}",
            Title = string.Empty, // invalid
            AuthorId = TestAuthorId,
            Genre = "Software",
            PublishYear = 2008
        };

        // Act
        var response = await PostBookAsync(request, Guid.NewGuid().ToString());

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateBook_WithNonExistentAuthor_Returns400()
    {
        // Arrange
        var request = new CreateBookRequest
        {
            ISBN = $"978{Random.Shared.NextInt64(1000000000, 9999999999)}",
            Title = "Orphan Book",
            AuthorId = Guid.NewGuid(), // does not exist
            Genre = "Fiction",
            PublishYear = 2020
        };

        // Act
        var response = await PostBookAsync(request, Guid.NewGuid().ToString());

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- READ ---

    [Fact]
    public async Task GetBookById_WhenBookExists_Returns200()
    {
        // Arrange — create a book first
        var createResponse = await PostBookAsync(CreateValidRequest(), Guid.NewGuid().ToString());
        var created = await createResponse.Content.ReadFromJsonAsync<BookResponse>();

        // Act
        var response = await _client.GetAsync($"/api/books/{created!.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var book = await response.Content.ReadFromJsonAsync<BookResponse>();
        Assert.Equal(created.Id, book!.Id);
        Assert.Equal(created.Title, book.Title);
    }

    [Fact]
    public async Task GetBookById_WhenBookDoesNotExist_Returns404()
    {
        // Act
        var response = await _client.GetAsync($"/api/books/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAllBooks_ReturnsPaginatedResponse()
    {
        // Arrange — create two books
        await PostBookAsync(CreateValidRequest("Paginated Book 1"), Guid.NewGuid().ToString());
        await PostBookAsync(CreateValidRequest("Paginated Book 2"), Guid.NewGuid().ToString());

        // Act
        var response = await _client.GetAsync("/api/books?pageNumber=1&pageSize=10");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var paged = await response.Content.ReadFromJsonAsync<PagedResponse<BookResponse>>();
        Assert.NotNull(paged);
        Assert.True(paged.TotalCount >= 2);
        Assert.Equal(1, paged.PageNumber);
    }

    // --- UPDATE ---

    [Fact]
    public async Task UpdateBook_WithValidData_Returns200()
    {
        // Arrange
        var createResponse = await PostBookAsync(CreateValidRequest(), Guid.NewGuid().ToString());
        var created = await createResponse.Content.ReadFromJsonAsync<BookResponse>();

        var updateRequest = new UpdateBookRequest
        {
            ISBN = "9780201633610",
            Title = "Updated Title",
            AuthorId = TestAuthorId,
            Genre = "Architecture",
            PublishYear = 1999,
            Description = "Updated description."
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/books/{created!.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<BookResponse>();
        Assert.Equal("Updated Title", updated!.Title);
        Assert.Equal("Architecture", updated.Genre);
    }

    [Fact]
    public async Task UpdateBook_WhenBookDoesNotExist_Returns404()
    {
        // Arrange
        var updateRequest = new UpdateBookRequest
        {
            ISBN = "9780201633610",
            Title = "Ghost Book",
            AuthorId = TestAuthorId,
            Genre = "Fiction",
            PublishYear = 2020
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/books/{Guid.NewGuid()}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- DELETE ---

    [Fact]
    public async Task DeleteBook_WhenBookExists_Returns204()
    {
        // Arrange
        var createResponse = await PostBookAsync(CreateValidRequest(), Guid.NewGuid().ToString());
        var created = await createResponse.Content.ReadFromJsonAsync<BookResponse>();

        // Act
        var response = await _client.DeleteAsync($"/api/books/{created!.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify it is actually gone
        var getResponse = await _client.GetAsync($"/api/books/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteBook_WhenBookDoesNotExist_Returns404()
    {
        // Act
        var response = await _client.DeleteAsync($"/api/books/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- BLOB STORAGE UPLOAD PATTERNS ---

    [Fact]
    public async Task UploadViaServer_WithValidFile_Returns200AndSetsCoverImageUrl()
    {
        // Arrange
        var createResponse = await PostBookAsync(CreateValidRequest(), Guid.NewGuid().ToString());
        var created = await createResponse.Content.ReadFromJsonAsync<BookResponse>();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", "cover.jpg");

        // Act
        var response = await _client.PostAsync($"/api/books/{created!.Id}/upload-server?assetType=cover", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var getResponse = await _client.GetAsync($"/api/books/{created.Id}");
        var updated = await getResponse.Content.ReadFromJsonAsync<BookResponse>();
        Assert.NotNull(updated?.CoverImageUrl);
        Assert.Contains("cover.jpg", updated.CoverImageUrl);
    }

    [Fact]
    public async Task GetSasToken_WhenBookExists_Returns200AndValidSasUri()
    {
        // Arrange
        var createResponse = await PostBookAsync(CreateValidRequest(), Guid.NewGuid().ToString());
        var created = await createResponse.Content.ReadFromJsonAsync<BookResponse>();

        // Act
        var response = await _client.PostAsync($"/api/books/{created!.Id}/sas-token?assetType=cover&fileName=direct.jpg", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("sasUri", json);
    }

    [Fact]
    public async Task GetSasToken_WhenBookDoesNotExist_Returns404()
    {
        // Act
        var response = await _client.PostAsync($"/api/books/{Guid.NewGuid()}/sas-token", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
