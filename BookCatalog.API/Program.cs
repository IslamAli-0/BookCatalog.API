using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Storage.Blobs;
using BookCatalog.API.Handlers;
using BookCatalog.API.Middleware;
using BookCatalog.API.Options;
using BookCatalog.Core.Interfaces;
using BookCatalog.Core.Models;
using BookCatalog.Core.Services;
using BookCatalog.Infrastructure.Data;
using BookCatalog.Infrastructure.Repositories;
using BookCatalog.Infrastructure.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Read graceful shutdown timeout from configuration (default: 30s)
var shutdownTimeoutSeconds = builder.Configuration.GetValue<int>("ShutdownTimeoutSeconds", 30);
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(shutdownTimeoutSeconds);
});

// Configure Serilog from appsettings.json + enrich with environment/correlation
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "BookCatalog.API")
    .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName));

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Database Context (SQL Server with Resilience & Retries)
// EnableRetryOnFailure handles transient network drops and Azure SQL Serverless cold starts (5 attempts, max 30s delay).
// CommandTimeout(60) ensures long-running queries fail gracefully rather than locking connections forever.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString,
        sqlServerOptionsAction: sqlOptions =>
        {
            sqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
            sqlOptions.CommandTimeout(60);
        }));

// Repositories & Services
builder.Services.AddScoped<IBookRepository, BookRepository>();
builder.Services.AddScoped<IBookService, BookService>();
builder.Services.AddScoped<ILendingRepository, LendingRepository>();
builder.Services.AddScoped<ILendingService, LendingService>();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// ── Health Checks ────────────────────────────────────────────────────────────
// /health/live  — Fast lightweight "self" check (no DB dependency, prevents container restart loops)
// /health/ready — Checks database readiness (tagged "ready" — gates traffic routing)
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddSqlServer(
        connectionString: connectionString,
        healthQuery: "SELECT 1;",
        name: "sql-server",
        timeout: TimeSpan.FromSeconds(10),
        tags: ["ready"]);

// ── Azure Blob Storage ───────────────────────────────────────────────────────
var blobConnectionString = builder.Configuration["AzureBlobStorage:ConnectionString"];
if (!string.IsNullOrWhiteSpace(blobConnectionString))
{
    builder.Services.AddSingleton(new BlobServiceClient(blobConnectionString));
}
else
{
    builder.Services.AddSingleton(new BlobServiceClient("UseDevelopmentStorage=true"));
}

builder.Services.AddScoped<IBlobService, BlobService>();

// ── Request Timeouts (ASP.NET Core) ─────────────────────────────────────────
// Global 30-second server-side timeout so slow downstream calls do not hold threads indefinitely.
builder.Services.AddRequestTimeouts(options =>
{
    options.DefaultPolicy = new Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutPolicy
    {
        Timeout = TimeSpan.FromSeconds(30)
    };
});

// ── OpenTelemetry — Distributed Tracing ─────────────────────────────────────
// Uses Azure Monitor OpenTelemetry when APPLICATIONINSIGHTS_CONNECTION_STRING is present.
// Gracefully falls back to local OTel tracing without exporter during local dev/tests.
var appInsightsConnStr = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(appInsightsConnStr))
{
    builder.Services.AddOpenTelemetry()
        .UseAzureMonitor(options =>
        {
            options.ConnectionString = appInsightsConnStr;
        })
        .WithTracing(tracing =>
        {
            tracing.AddEntityFrameworkCoreInstrumentation(o =>
            {
                o.SetDbStatementForText = true;
                o.SetDbStatementForStoredProcedure = true;
            });
        });
}
else
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("BookCatalog.API"))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation(o =>
            {
                o.Filter = ctx =>
                    !ctx.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);
            })
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation());
}

var app = builder.Build();

// ── Startup: apply pending EF Core migrations / seed dev data ───────────────
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

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
        try
        {
            var blobService = scope.ServiceProvider.GetRequiredService<IBlobService>();
            const string container = "book-assets";

            // Upload a minimal 1x1 transparent PNG as sample cover
            var pngBytes = new byte[]
            {
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
                0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
                0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
                0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
                0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
                0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
                0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
                0x42, 0x60, 0x82
            };
            await using var pngStream = new MemoryStream(pngBytes);
            await blobService.UploadAsync(container, "samples/sample-cover.png", pngStream, "image/png");

            // Upload a minimal valid PDF
            var pdfBytes = System.Text.Encoding.ASCII.GetBytes(
                "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n" +
                "2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
                "3 0 obj<</Type/Page/MediaBox[0 0 3 3]>>endobj\n" +
                "xref\n0 4\n0000000000 65535 f \n0000000009 00000 n \n0000000052 00000 n \n0000000096 00000 n \n" +
                "trailer<</Size 4/Root 1 0 R>>\nstartxref\n147\n%%EOF");
            await using var pdfStream = new MemoryStream(pdfBytes);
            await blobService.UploadAsync(container, "samples/sample-book.pdf", pdfStream, "application/pdf");

            Log.Information("Sample blob assets seeded to container '{Container}'", container);
        }
        catch (Exception ex)
        {
            // Do not crash startup if Azurite is not running locally — just warn.
            Log.Warning(ex, "Could not seed sample blob assets (Azure Storage may not be reachable locally)");
        }
    }
}

// ── Middleware pipeline ───────────────────────────────────────────────────────
// Must go before controllers so it can catch their exceptions.
app.UseExceptionHandler();

// Request timeouts — must be early in the pipeline before controllers execute.
app.UseRequestTimeouts();

// Correlation ID — echo X-Correlation-ID + trace-id on every response.
app.UseMiddleware<CorrelationIdMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Liveness: "is the process alive?" — runs fast "self" check, no DB dependency.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = hc => hc.Tags.Contains("live")
});

// Readiness: "can the service do work?" — checks "ready" tagged checks (SQL Server).
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = hc => hc.Tags.Contains("ready")
});

app.UseHttpsRedirection();

// Serilog request logging — correlates HTTP request/response in structured logs.
app.UseSerilogRequestLogging();

app.UseAuthorization();

app.MapControllers();

// ── Application lifecycle logging ────────────────────────────────────────────
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(() =>
    Log.Information("Application started. Graceful shutdown timeout: {ShutdownTimeout}s", shutdownTimeoutSeconds));
lifetime.ApplicationStopping.Register(() =>
    Log.Information("Application is shutting down. Completing in-flight requests..."));
lifetime.ApplicationStopped.Register(() =>
    Log.Information("Application stopped. All in-flight requests completed."));

app.Run();

public partial class Program { }