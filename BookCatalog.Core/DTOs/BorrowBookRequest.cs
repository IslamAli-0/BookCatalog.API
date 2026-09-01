using BookCatalog.Core.Attributes;
using System.ComponentModel.DataAnnotations;

namespace BookCatalog.Core.DTOs;

public record BorrowBookRequest
{
    [Required(ErrorMessage = "The user ID is required.")]
    [NotEmpty(ErrorMessage = "The user ID cannot be empty.")]
    public Guid UserId { get; init; }
}
