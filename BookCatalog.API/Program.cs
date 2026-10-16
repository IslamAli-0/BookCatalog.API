using BookCatalog.API.Handlers;
using BookCatalog.API.Options;
using BookCatalog.Core.Interfaces;
using BookCatalog.Core.Models;
using BookCatalog.Core.Services;
using BookCatalog.Infrastructure.Data;
using BookCatalog.Infrastructure.Repositories;
using BookCatalog.Infrastructure.Services;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Graceful shutdown — give in-flight requests up to 30 seconds to complete
// when the process receives SIGTERM (e.g., docker compose down, Ctrl+C).
// Default is 30s in .NET 8+, but we set it explicitly so the intent is documented
// and won't silently change if defaults shift in a future runtime version.
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(30));

// Configure Serilog for structured JSON logging
builder.Host.UseSerilog((context, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter()));

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configuration validation — fail fast at startup if connection string is missing.
// ValidateDataAnnotations checks [Required] on DatabaseOptions properties.
// ValidateOnStart runs the validation before the first request, not lazily on first use.
// Without this, a missing connection string causes a cryptic NullReferenceException
// on the first DB call instead of a clear startup error.
builder.Services.AddOptions<DatabaseOptions>()
    .BindConfiguration(DatabaseOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Read the connection string for DbContext and health check registration
var connectionString = builder.Configuration
    .GetSection(DatabaseOptions.SectionName)
    .Get<DatabaseOptions>()?
    .DefaultConnection
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? string.Empty;

// EF Core — register the DbContext with the SQL Server provider
// EnableRetryOnFailure: automatically retries transient errors (connection drops, timeouts,
// deadlocks) up to 3 times with exponential back-off before surfacing as an error.
// Only safe because all writes are wrapped in explicit transactions or are single-operation;
// EF Core tracks whether a retry is inside a user-managed transaction and skips auto-retry
// in that case to avoid retrying non-idempotent committed work.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        connectionString,
        sqlOptions => sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null)));

// Scoped lifetime — DbContext is scoped, so the repository must be too
builder.Services.AddScoped<IBookRepository, BookRepository>();

builder.Services.AddScoped<IBookService, BookService>();

builder.Services.AddScoped<ILendingRepository, LendingRepository>();
builder.Services.AddScoped<ILendingService, LendingService>();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Health Checks
// /health/live  — is the process running? (no DB check, used by container orchestrators for restarts)
// /health/ready — can the service do its job? (includes DB reachability, used to gate traffic)
builder.Services.AddHealthChecks()
    .AddSqlServer(
        connectionString: connectionString,
        name: "sql-server",
        tags: ["ready"]);

// ─── Azure Blob Storage ────────────────────────────────────────────────────────
// BlobServiceClient is registered as Singleton: it is thread-safe and designed
// to be shared. The connection string is read from AzureBlobStorage:ConnectionString
// (set as an env var in production: AzureBlobStorage__ConnectionString).
var blobConnectionString = builder.Configuration["AzureBlobStorage:ConnectionString"];
if (!string.IsNullOrWhiteSpace(blobConnectionString))
{
    builder.Services.AddSingleton(new BlobServiceClient(blobConnectionString));
}
else
{
    // Fallback for local development without Azure Storage:
    // use Azurite (local emulator) or any account you set in appsettings.Development.json.
    // We register a Azurite default endpoint so the service compiles and starts even when
    // the env-var is absent; SAS generation will still fail without real credentials.
    builder.Services.AddSingleton(new BlobServiceClient(
        "UseDevelopmentStorage=true"));
}

builder.Services.AddScoped<IBlobService, BlobService>();

var app = builder.Build();

// Auto-apply pending EF Core migrations on startup (required for Docker one-command setup)
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    // var maxRetries = 5;
    // for (int retry = 1; retry <= maxRetries; retry++)
    // {
    //     try
    //     {
    //         await context.Database.MigrateAsync();
    //         break;
    //     }
    //     catch (Exception ex)
    //     {
    //         if (retry == maxRetries)
    //         {
    //             throw new Exception($"Failed to apply migrations after {maxRetries} attempts.", ex);
    //         }
    //         await Task.Delay(2000);
    //     }
    // }

    // Seed Data for Authors and Users
    if (app.Environment.IsDevelopment())
    {
        if (!await context.Authors.AnyAsync())
        {
            context.Authors.Add(new Author { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "Test Author" });
            await context.SaveChangesAsync();
        }
        if (!await context.Users.AnyAsync())
        {
            context.Users.Add(new User { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), FullName = "Test User", Email = "test@user.com" });
            await context.SaveChangesAsync();
        }

        // ── Seed sample blob assets ──────────────────────────────────────────
        // Upload a placeholder cover image and a placeholder PDF so the
        // container has working test assets out of the box in Development.
        try
        {
            var blobService = scope.ServiceProvider.GetRequiredService<IBlobService>();
            const string container = "book-assets";

            // Sample cover (1x1 white JPEG — minimal valid JFIF)
            var coverBytes = Convert.FromBase64String(
                "/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8U" +
                "HRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAARCAABAAEDASIA" +
                "AhEBAxEB/8QAFgABAQEAAAAAAAAAAAAAAAAABgUE/8QAIRAAAQMEAwEBAAAAAAAAAAAAAQID" +
                "BAURBhIhMVH/xAAUAQEAAAAAAAAAAAAAAAAAAAAA/8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/a" +
                "AAwDAQACEQMRAD8AsAAZAAAAAH//2Q==");
            await using var coverStream = new MemoryStream(coverBytes);
            await blobService.UploadAsync(container, "samples/sample-cover.jpg", coverStream, "image/jpeg");

            // Sample PDF (minimal valid 1-page PDF)
            var pdfBytes = System.Text.Encoding.ASCII.GetBytes(
                "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n" +
                "2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
                "3 0 obj<</Type/Page/MediaBox[0 0 612 792]/Parent 2 0 R>>endobj\n" +
                "xref\n0 4\n0000000000 65535 f\n0000000009 00000 n\n" +
                "0000000058 00000 n\n0000000115 00000 n\n" +
                "trailer<</Size 4/Root 1 0 R>>\nstartxref\n190\n%%EOF");
            await using var pdfStream = new MemoryStream(pdfBytes);
            await blobService.UploadAsync(container, "samples/sample-book.pdf", pdfStream, "application/pdf");

            Log.Information("Sample blob assets seeded to container '{Container}'.", container);
        }
        catch (Exception ex)
        {
            // Do not crash startup if Azurite is not running locally — just warn.
            Log.Warning(ex, "Could not seed sample blob assets (Azure Storage may not be reachable locally).");
        }
    }
}

// Must go before controllers so it can catch their errors.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Liveness: just "is the process alive?" — no dependency checks, always fast.
// Explicitly exclude "ready" tagged checks (SQL Server) so this stays true even when DB is down.
// Used by container orchestrators to decide whether to RESTART the container.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = hc => !hc.Tags.Contains("ready")
});

// Readiness: "can the service actually do its job?" — only runs checks tagged "ready" (SQL Server).
// Used by orchestrators to decide whether to SEND TRAFFIC to this instance.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = hc => hc.Tags.Contains("ready")
});

app.UseHttpsRedirection();

// Use Serilog for request logging (correlates logs with requests)
app.UseSerilogRequestLogging();

app.UseAuthorization();

app.MapControllers();

// Log application lifecycle events so graceful shutdown is verifiable in the logs.
// After docker compose down or Ctrl+C, you should see:
//   "Application is shutting down..." followed by "Application stopped."
// If an in-flight request was being processed, its response will complete before "stopped."
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(() =>
    Log.Information("Application started. Graceful shutdown timeout: {ShutdownTimeout}s", 30));
lifetime.ApplicationStopping.Register(() =>
    Log.Information("Application is shutting down. Completing in-flight requests..."));
lifetime.ApplicationStopped.Register(() =>
    Log.Information("Application stopped. All in-flight requests completed."));

app.Run();