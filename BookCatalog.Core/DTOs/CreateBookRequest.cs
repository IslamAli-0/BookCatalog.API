using BookCatalog.Core.Attributes;
using System.ComponentModel.DataAnnotations;

namespace BookCatalog.Core.DTOs;

public record CreateBookRequest
{
    [Required(ErrorMessage = "ISBN is required.")]
    [RegularExpression(@"^(97(8|9))?\d{9}(\d|X)$", ErrorMessage = "Please provide a valid 10 or 13-digit ISBN.")]
    public string ISBN { get; init; } = string.Empty;

    [Required(ErrorMessage = "The book title is required.")]
    [MaxLength(100, ErrorMessage = "Title is too long. It cannot exceed 100 characters.")]
    public string Title { get; init; } = string.Empty;

    [Required(ErrorMessage = "The author's name is required.")]
    [MaxLength(50, ErrorMessage = "Author name is too long. It cannot exceed 50 characters.")]
    public string Author { get; init; } = string.Empty;

    [Required(ErrorMessage = "Genre is required to help categorize the book.")]
    [MaxLength(30, ErrorMessage = "Genre cannot exceed 30 characters.")]
    public string Genre { get; init; } = string.Empty;

    [ValidPublishYear(ErrorMessage = "Publish year must be valid and cannot be far in the future.")]
    public int PublishYear { get; init; }

    [MaxLength(500, ErrorMessage = "Description is too long. It cannot exceed 500 characters.")]
    public string? Description { get; init; }
}
