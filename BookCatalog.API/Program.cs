using BookCatalog.API.Handlers;
using BookCatalog.Core.Interfaces;
using BookCatalog.Core.Services;
using BookCatalog.Infrastructure.Data;
using BookCatalog.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// EF Core — register the DbContext with the SQL Server provider
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Scoped lifetime — DbContext is scoped, so the repository must be too
builder.Services.AddScoped<IBookRepository, BookRepository>();

builder.Services.AddScoped<IBookService, BookService>();

builder.Services.AddScoped<ILendingRepository, LendingRepository>();
builder.Services.AddScoped<ILendingService, LendingService>();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

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
    if (!context.Authors.Any())
    {
        context.Authors.Add(new BookCatalog.Core.Models.Author { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "Test Author" });
        context.SaveChanges();
    }
    if (!context.Users.Any())
    {
        context.Users.Add(new BookCatalog.Core.Models.User { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), FullName = "Test User", Email = "test@user.com" });
        context.SaveChanges();
    }
}

// Must go before controllers so it can catch their errors.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();