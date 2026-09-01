using System.ComponentModel.DataAnnotations;

namespace BookCatalog.Core.Attributes;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public class NotEmptyAttribute : ValidationAttribute
{
    public const string DefaultErrorMessage = "The {0} field must not be empty.";

    public NotEmptyAttribute() : base(DefaultErrorMessage) { }

    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true; // Use [Required] for null checks
        }

        switch (value)
        {
            case Guid guid:
                return guid != Guid.Empty;
            case string s:
                return !string.IsNullOrWhiteSpace(s);
            default:
                return true;
        }
    }
}
