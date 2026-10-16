using BookCatalog.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace BookCatalog.IntegrationTests;

/// <summary>
/// Replaces the production SQL Server with a Testcontainers SQL Server instance.
/// Each test class that uses this factory gets a fresh, real database.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _dbContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", _dbContainer.GetConnectionString());

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));

            if (descriptor != null)
                services.Remove(descriptor);

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(_dbContainer.GetConnectionString()));

            var blobDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(BookCatalog.Core.Interfaces.IBlobService));
            if (blobDescriptor != null)
                services.Remove(blobDescriptor);
            services.AddSingleton<BookCatalog.Core.Interfaces.IBlobService, FakeBlobService>();

            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.EnsureCreated();
        });

        builder.UseEnvironment("Development");
    }

    private class FakeBlobService : BookCatalog.Core.Interfaces.IBlobService
    {
        public Task<Uri> UploadAsync(string containerName, string blobPath, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new Uri($"https://127.0.0.1:10000/devstoreaccount1/{containerName}/{blobPath}"));
        }

        public Uri GenerateSasUri(string containerName, string blobPath, int validForMinutes = 15)
        {
            return new Uri($"https://127.0.0.1:10000/devstoreaccount1/{containerName}/{blobPath}?sv=2024-11-04&se=2026-10-16T12:15:00Z&sr=b&sp=cw&sig=fake");
        }
    }

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();
    }

    public new async Task DisposeAsync()
    {
        await _dbContainer.StopAsync();
        await base.DisposeAsync();
    }
}
