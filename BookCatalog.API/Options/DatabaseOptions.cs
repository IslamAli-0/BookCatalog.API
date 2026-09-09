using System.ComponentModel.DataAnnotations;

namespace BookCatalog.API.Options;

/// <summary>
/// Strongly-typed representation of the ConnectionStrings configuration section.
/// Validated at startup via the Options pattern — the app refuses to start if
/// DefaultConnection is missing or empty, preventing a confusing first-request crash.
/// </summary>
public class DatabaseOptions
{
    // The configuration section this class maps to (used in Program.cs BindConfiguration call)
    public const string SectionName = "ConnectionStrings";

    /// <summary>
    /// The full ADO.NET connection string for the primary SQL Server database.
    /// Maps to ConnectionStrings:DefaultConnection in appsettings.json / environment variables.
    /// </summary>
    [Required(ErrorMessage = "ConnectionStrings:DefaultConnection is required. " +
        "Set it in appsettings.json, an environment variable, or user secrets.")]
    public string DefaultConnection { get; init; } = string.Empty;
}
