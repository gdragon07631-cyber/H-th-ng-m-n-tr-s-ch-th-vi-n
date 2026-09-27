using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Services;

public sealed class AuthorService(
    ApplicationDbContext dbContext,
    ILogger<AuthorService> logger) : IAuthorService
{
    public async Task<AuthorCreationOutcome> CreateAuthorAsync(
        string name,
        string? note,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return AuthorCreationOutcome.Failed("Họ tên tác giả không được để trống.");
        }

        var trimmedName = name.Trim();
        var normalizedName = trimmedName.ToUpperInvariant();

        // Kiểm tra tên đã tồn tại hay chưa
        var exists = await dbContext.Authors
            .AnyAsync(a => a.Name.ToUpper() == normalizedName, cancellationToken);

        if (exists)
        {
            logger.LogWarning("Thêm tác giả bị từ chối: Tên tác giả '{Name}' đã tồn tại.", trimmedName);
            return AuthorCreationOutcome.Duplicate($"Tác giả '{trimmedName}' đã tồn tại trong hệ thống.");
        }

        var author = new Author
        {
            Name = trimmedName,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            Status = AuthorStatus.Active,
            CreatedAtUtc = DateTime.UtcNow
        };

        try
        {
            dbContext.Authors.Add(author);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Thêm tác giả thành công: {Name} (Id: {Id})", author.Name, author.Id);
            return AuthorCreationOutcome.Success(author);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Lỗi cập nhật CSDL khi tạo tác giả '{Name}'. Kiểm tra trùng tên.", trimmedName);
            return AuthorCreationOutcome.Duplicate($"Tác giả '{trimmedName}' đã tồn tại trong hệ thống.");
        }
    }

    public async Task<IReadOnlyList<Author>> GetAllAuthorsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Authors
            .OrderByDescending(a => a.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Author>> GetActiveAuthorsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Authors
            .Where(a => a.Status == AuthorStatus.Active)
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);
    }
}
