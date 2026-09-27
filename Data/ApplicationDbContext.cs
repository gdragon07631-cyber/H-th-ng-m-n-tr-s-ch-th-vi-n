using Microsoft.EntityFrameworkCore;
using Project.Models;

namespace Project.Data;

/// <summary>
/// EF Core context for the application. Business entities can be added here as stories are implemented.
/// </summary>
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<AdminAccount> AdminAccounts => Set<AdminAccount>();
    public DbSet<LoginLog> LoginLogs => Set<LoginLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ReaderAccount> ReaderAccounts => Set<ReaderAccount>();
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Author>(entity =>
        {
            entity.HasIndex(author => author.Name).IsUnique();
            entity.Property(author => author.Name).HasMaxLength(150).IsRequired();
            entity.Property(author => author.Note).HasMaxLength(500);
            entity.Property(author => author.Status).HasMaxLength(50).HasDefaultValue("Hoạt động").IsRequired();
            entity.Property(author => author.CreatedAtUtc).HasColumnType("datetime2");
        });

        modelBuilder.Entity<Book>(entity =>
        {
            entity.Property(b => b.Title).HasMaxLength(250).IsRequired();
            entity.Property(b => b.Isbn).HasMaxLength(50);
            entity.Property(b => b.Description).HasMaxLength(500);
            entity.Property(b => b.CreatedAtUtc).HasColumnType("datetime2");
            entity.HasOne(b => b.Author)
                .WithMany(a => a.Books)
                .HasForeignKey(b => b.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(b => b.Category)
                .WithMany(c => c.Books)
                .HasForeignKey(b => b.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasIndex(category => category.Name).IsUnique();
            entity.Property(category => category.Name).HasMaxLength(150).IsRequired();
            entity.Property(category => category.Status).HasMaxLength(50).HasDefaultValue("Hoạt động").IsRequired();
            entity.Property(category => category.CreatedAtUtc).HasColumnType("datetime2");
            entity.HasOne(category => category.Parent)
                .WithMany(category => category.Children)
                .HasForeignKey(category => category.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AdminAccount>(entity =>
        {
            entity.HasIndex(account => account.Email).IsUnique();
            entity.Property(account => account.Email).HasMaxLength(256).IsRequired();
            entity.Property(account => account.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(account => account.IsActive).HasDefaultValue(true);
            entity.Property(account => account.FailedLoginAttempts).HasDefaultValue(0);
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_AdminAccounts_FailedLoginAttempts_NonNegative", "[FailedLoginAttempts] >= 0"));
        });

        modelBuilder.Entity<LoginLog>(entity =>
        {
            entity.HasIndex(log => log.AttemptedAtUtc);
            entity.HasIndex(log => log.Email);
            entity.Property(log => log.Email).HasMaxLength(256).IsRequired();
            entity.Property(log => log.IpAddress).HasMaxLength(45);
            entity.Property(log => log.Status).HasMaxLength(32).IsRequired();
            entity.HasOne(log => log.AdminAccount)
                .WithMany()
                .HasForeignKey(log => log.AdminAccountId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasIndex(token => new { token.AdminAccountId, token.ExpiresAtUtc });
            entity.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(token => token.ReplacedByTokenHash).HasMaxLength(64);
            entity.HasOne(token => token.AdminAccount)
                .WithMany()
                .HasForeignKey(token => token.AdminAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReaderAccount>(entity =>
        {
            entity.HasIndex(reader => reader.Email);
            entity.HasIndex(reader => reader.StudentOrStaffCode);
            entity.Property(reader => reader.FullName).HasMaxLength(100).IsRequired();
            entity.Property(reader => reader.Email).HasMaxLength(256).IsRequired();
            entity.Property(reader => reader.PhoneNumber).HasMaxLength(20).IsRequired();
            entity.Property(reader => reader.StudentOrStaffCode).HasMaxLength(50).IsRequired();
            entity.Property(reader => reader.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(reader => reader.Status).HasMaxLength(50).HasDefaultValue("Chờ duyệt").IsRequired();
        });
    }
}
