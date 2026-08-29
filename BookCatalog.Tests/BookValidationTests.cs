using System.ComponentModel.DataAnnotations;
using BookCatalog.Core.Attributes;
using BookCatalog.Core.DTOs;

namespace BookCatalog.Tests;

/// <summary>
/// Unit tests for the <see cref="ValidPublishYearAttribute"/> custom validation attribute
/// and the surrounding DTO-level validation rules on <see cref="CreateBookRequest"/>.
///
/// No mocks are needed here: the attribute is a pure function of its input — zero
/// external dependencies means zero fakes required.
/// </summary>
public class BookValidationTests
{
    // ── Helper: runs the attribute in isolation (same code-path as IsValid) ─
    private static ValidationResult? Validate(object? value)
    {
        var attribute = new ValidPublishYearAttribute();
        var context   = new ValidationContext(new object()); // owner object is irrelevant here
        return attribute.GetValidationResult(value, context);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Happy paths — valid years that must pass
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void IsValid_WithTypicalHistoricalYear_ReturnsSuccess()
    {
        // Arrange
        const int year = 2020;

        // Act
        var result = Validate(year);

        // Assert
        Assert.Equal(ValidationResult.Success, result);
    }

    [Fact]
    public void IsValid_WithCurrentYear_ReturnsSuccess()
    {
        // Arrange — a book published this year must always be valid
        int year = DateTime.UtcNow.Year;

        // Act
        var result = Validate(year);

        // Assert
        Assert.Equal(ValidationResult.Success, result);
    }

    [Fact]
    public void IsValid_WithOneYearAhead_ReturnsSuccess()
    {
        // Arrange — pre-orders (current year + 1) are explicitly allowed by the attribute
        int year = DateTime.UtcNow.Year + 1;

        // Act
        var result = Validate(year);

        // Assert
        Assert.Equal(ValidationResult.Success, result);
    }

    [Fact]
    public void IsValid_WithMinimumBoundaryYear_ReturnsSuccess()
    {
        // Arrange — 1400 is the earliest accepted year (dawn of movable-type printing)
        const int year = 1400;

        // Act
        var result = Validate(year);

        // Assert
        Assert.Equal(ValidationResult.Success, result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Sad paths — invalid years that must fail with the correct error message
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void IsValid_WithFarFutureYear_ReturnsValidationError()
    {
        // Arrange
        const int year = 3000;

        // Act
        var result = Validate(year);

        // Assert — a ValidationResult (not null / Success) with a meaningful message
        Assert.NotNull(result);
        Assert.NotEqual(ValidationResult.Success, result);
        Assert.Contains("1400", result!.ErrorMessage); // lower bound mentioned in the message
    }

    [Fact]
    public void IsValid_WithDistantPastYear_ReturnsValidationError()
    {
        // Arrange — year 500 pre-dates the printing press; should be rejected
        const int year = 500;

        // Act
        var result = Validate(year);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(ValidationResult.Success, result);
        Assert.Contains("1400", result!.ErrorMessage);
    }

    [Fact]
    public void IsValid_WithYearJustBelowMinimumBoundary_ReturnsValidationError()
    {
        // Arrange — 1399 is one year below the lower bound (off-by-one boundary test)
        const int year = 1399;

        // Act
        var result = Validate(year);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(ValidationResult.Success, result);
    }

    [Fact]
    public void IsValid_WithYearTwoYearsAhead_ReturnsValidationError()
    {
        // Arrange — current year + 2 exceeds the pre-order window (off-by-one boundary test)
        int year = DateTime.UtcNow.Year + 2;

        // Act
        var result = Validate(year);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(ValidationResult.Success, result);
    }

    [Fact]
    public void IsValid_WithZeroYear_ReturnsValidationError()
    {
        // Arrange — year 0 is nonsensical for a published book
        const int year = 0;

        // Act
        var result = Validate(year);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(ValidationResult.Success, result);
    }

    [Fact]
    public void IsValid_WithNegativeYear_ReturnsValidationError()
    {
        // Arrange — negative years are unambiguously invalid
        const int year = -100;

        // Act
        var result = Validate(year);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(ValidationResult.Success, result);
    }

    [Fact]
    public void IsValid_WithNonIntegerValue_ReturnsInvalidFormatError()
    {
        // Arrange — the attribute's type guard must reject non-int objects
        const string value = "twenty-twenty";

        // Act
        var result = Validate(value);

        // Assert — specific "Invalid year format." message defined in the attribute
        Assert.NotNull(result);
        Assert.Equal("Invalid year format.", result!.ErrorMessage);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DTO-level integration: ValidPublishYear inside CreateBookRequest
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CreateBookRequest_WithValidPublishYear_PassesModelValidation()
    {
        // Arrange — a fully valid request DTO
        var request = new CreateBookRequest
        {
            ISBN        = "9780132350884",
            Title       = "Clean Code",
            Author      = "Robert C. Martin",
            Genre       = "Technology",
            PublishYear = 2008
        };
        var validationResults = new List<ValidationResult>();
        var context           = new ValidationContext(request);

        // Act
        bool isValid = Validator.TryValidateObject(request, context, validationResults, validateAllProperties: true);

        // Assert
        Assert.True(isValid);
        Assert.Empty(validationResults);
    }

    [Fact]
    public void CreateBookRequest_WithFuturePublishYear_FailsModelValidation()
    {
        // Arrange — only the year is invalid; all other fields are correct
        var request = new CreateBookRequest
        {
            ISBN        = "9780132350884",
            Title       = "Future Book",
            Author      = "Time Traveler",
            Genre       = "Science Fiction",
            PublishYear = 3000
        };
        var validationResults = new List<ValidationResult>();
        var context           = new ValidationContext(request);

        // Act
        bool isValid = Validator.TryValidateObject(request, context, validationResults, validateAllProperties: true);

        // Assert — exactly one error, and the message is the overridden one declared on the attribute
        // Note: MemberNames is empty because ValidPublishYearAttribute does not set it via
        // ValidationResult(message, memberNames) — checking the error message is the correct assertion.
        Assert.False(isValid);
        Assert.Single(validationResults);
        Assert.Equal(
            "Publish year must be valid and cannot be far in the future.",
            validationResults[0].ErrorMessage);
    }

    [Fact]
    public void CreateBookRequest_WithCustomErrorMessage_SurfacesOverriddenMessage()
    {
        // Arrange — CreateBookRequest overrides ErrorMessage on [ValidPublishYear]
        // The attribute falls back to the custom message when one is provided.
        var request = new CreateBookRequest
        {
            ISBN        = "9780132350884",
            Title       = "Bad Year",
            Author      = "Someone",
            Genre       = "Fiction",
            PublishYear = 1300 // below minYear of 1400
        };
        var validationResults = new List<ValidationResult>();
        var context           = new ValidationContext(request);

        // Act
        Validator.TryValidateObject(request, context, validationResults, validateAllProperties: true);

        // Assert — the overridden ErrorMessage is what the attribute returns
        Assert.Single(validationResults);
        Assert.Equal(
            "Publish year must be valid and cannot be far in the future.",
            validationResults[0].ErrorMessage);
    }
}
