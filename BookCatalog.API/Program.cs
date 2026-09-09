using BookCatalog.API.Handlers;
using BookCatalog.API.Options;
using BookCatalog.Core.Interfaces;
using BookCatalog.Core.Models;
using BookCatalog.Core.Services;
using BookCatalog.Infrastructure.Data;
using BookCatalog.Infrastructure.Repositories;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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

// Resolve the validated connection string once for use below
var connectionString = builder.Services
    .BuildServiceProvider()
    .GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>()
    .Value
    .DefaultConnection;

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

var app = builder.Build();

// Auto-apply pending EF Core migrations on startup (required for Docker one-command setup)
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var maxRetries = 5;
    for (int retry = 1; retry <= maxRetries; retry++)
    {
        try
        {
            await context.Database.MigrateAsync();
            break;
        }
        catch (Exception ex)
        {
            if (retry == maxRetries)
            {
                throw new Exception($"Failed to apply migrations after {maxRetries} attempts.", ex);
            }
            await Task.Delay(2000);
        }
    }

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

app.UseAuthorization();

app.MapControllers();

app.Run();