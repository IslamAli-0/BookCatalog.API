using BookCatalog.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BookCatalog.Infrastructure.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Loan> Loans => Set<Loan>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Author -> Books (One-to-Many)
        modelBuilder.Entity<Author>(entity =>
        {
            entity.HasKey(a => a.Id);

            entity.Property(a => a.Name)
                  .IsRequired()
                  .HasMaxLength(200);

            entity.Property(a => a.Biography)
                  .HasMaxLength(2000);

            entity.HasMany(a => a.Books)
                  .WithOne(b => b.Author)
                  .HasForeignKey(b => b.AuthorId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // Book configuration
        modelBuilder.Entity<Book>(entity =>
        {
            entity.HasKey(b => b.Id);

            entity.Property(b => b.ISBN)
                  .IsRequired()
                  .HasMaxLength(13);

            entity.Property(b => b.Title)
                  .IsRequired()
                  .HasMaxLength(200);

            entity.Property(b => b.Genre)
                  .IsRequired()
                  .HasMaxLength(50);

            entity.Property(b => b.Description)
                  .HasMaxLength(2000);

            entity.Property(b => b.RowVersion)
                  .IsRowVersion();

            entity.HasIndex(b => b.ISBN)
                  .IsUnique();
        });

        // User -> Loans (One-to-Many)
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);

            entity.Property(u => u.Email)
                  .IsRequired()
                  .HasMaxLength(320);

            entity.Property(u => u.FullName)
                  .IsRequired()
                  .HasMaxLength(200);

            entity.HasIndex(u => u.Email)
                  .IsUnique();

            entity.HasMany(u => u.Loans)
                  .WithOne(l => l.User)
                  .HasForeignKey(l => l.UserId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // Loan configuration
        modelBuilder.Entity<Loan>(entity =>
        {
            entity.HasKey(l => l.Id);

            entity.HasOne(l => l.Book)
                  .WithMany(b => b.LoanHistory)
                  .HasForeignKey(l => l.BookId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(l => l.User)
                  .WithMany(u => u.Loans)
                  .HasForeignKey(l => l.UserId)
                  .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
