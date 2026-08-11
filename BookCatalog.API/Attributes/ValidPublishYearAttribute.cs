using System.ComponentModel.DataAnnotations;

namespace BookCatalog.API.Attributes;

public class ValidPublishYearAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is int year)
        {
            // Allow books from the dawn of printing up to 1 year in the future (for pre-orders)
            int currentYear = DateTime.UtcNow.Year;
            int maxYear = currentYear + 1;
            int minYear = 1400;

            if (year < minYear || year > maxYear)
            {
                return new ValidationResult(ErrorMessage ?? $"Publish year must be between {minYear} and {maxYear}.");
            }

            return ValidationResult.Success;
        }

        return new ValidationResult("Invalid year format.");
    }
}