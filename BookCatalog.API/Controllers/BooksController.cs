using BookCatalog.API.DTOs;
using BookCatalog.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookCatalog.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BooksController(IBookService bookService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var books = await bookService.GetAllBooksAsync();

        return Ok(books); 
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var book = await bookService.GetBookByIdAsync(id);

        if (book == null)
        {
            return NotFound(new { message = $"Book with ID {id} was not found." }); 
        }

        return Ok(book); 
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBookRequest request)
    {
        var createdBook = await bookService.CreateBookAsync(request);

        // 201 Created. This automatically adds a "Location" header to the HTTP response
        // pointing to the exact URL where the client can fetch this newly created book.
        return CreatedAtAction(nameof(GetById), new { id = createdBook.Id }, createdBook);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBookRequest request)
    {
        var updatedBook = await bookService.UpdateBookAsync(id, request);

        if (updatedBook == null)
        {
            return NotFound(new { message = $"Book with ID {id} was not found." }); 
        }

        return Ok(updatedBook); 
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await bookService.DeleteBookAsync(id);

        if (!deleted)
        {
            return NotFound(new { message = $"Book with ID {id} was not found." }); 
        }

        return NoContent(); // 204 No Content 
    }
}