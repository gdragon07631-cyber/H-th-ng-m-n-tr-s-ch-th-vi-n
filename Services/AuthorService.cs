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

    public async Task<AuthorUpdateOutcome> UpdateAuthorAsync(
        int id,
        string name,
        string? note,
        CancellationToken cancellationToken = default)
    {
        var author = await dbContext.Authors.FindAsync([id], cancellationToken);
        if (author == null)
        {
            logger.LogWarning("Cập nhật tác giả thất bại: Không tìm thấy tác giả #{Id}", id);
            return AuthorUpdateOutcome.NotFound("Không tìm thấy thông tin tác giả cần cập nhật.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return AuthorUpdateOutcome.Failed("Họ tên tác giả không được để trống.");
        }

        var trimmedName = name.Trim();
        var normalizedName = trimmedName.ToUpperInvariant();

        // Kiểm tra tên trùng với tác giả khác (loại trừ chính tác giả đang cập nhật)
        var isDuplicate = await dbContext.Authors
            .AnyAsync(a => a.Id != id && a.Name.ToUpper() == normalizedName, cancellationToken);

        if (isDuplicate)
        {
            logger.LogWarning("Cập nhật tác giả #{Id} bị từ chối: Tên tác giả '{Name}' đã tồn tại.", id, trimmedName);
            return AuthorUpdateOutcome.Duplicate("Tên tác giả đã tồn tại.");
        }

        author.Name = trimmedName;
        author.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Cập nhật tác giả #{Id} thành công: {Name}", author.Id, author.Name);
            return AuthorUpdateOutcome.Success(author);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Lỗi cập nhật CSDL khi sửa tác giả #{Id} với tên '{Name}'.", id, trimmedName);
            return AuthorUpdateOutcome.Duplicate("Tên tác giả đã tồn tại.");
        }
    }

    public async Task<AuthorStatusOutcome> ToggleAuthorStatusAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var author = await dbContext.Authors.FindAsync([id], cancellationToken);
        if (author == null)
        {
            return AuthorStatusOutcome.NotFound("Không tìm thấy thông tin tác giả.");
        }

        author.Status = author.Status == AuthorStatus.Active
            ? AuthorStatus.Inactive
            : AuthorStatus.Active;

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Đã chuyển trạng thái tác giả #{Id} '{Name}' sang '{Status}'.", author.Id, author.Name, author.Status);
        return AuthorStatusOutcome.Success(author);
    }

    public async Task<AuthorStatusOutcome> SetAuthorStatusAsync(
        int id,
        string status,
        CancellationToken cancellationToken = default)
    {
        var author = await dbContext.Authors.FindAsync([id], cancellationToken);
        if (author == null)
        {
            return AuthorStatusOutcome.NotFound("Không tìm thấy thông tin tác giả.");
        }

        if (status != AuthorStatus.Active && status != AuthorStatus.Inactive)
        {
            return AuthorStatusOutcome.Failed($"Trạng thái không hợp lệ: '{status}'.");
        }

        author.Status = status;
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Đã đặt trạng thái tác giả #{Id} '{Name}' thành '{Status}'.", author.Id, author.Name, author.Status);
        return AuthorStatusOutcome.Success(author);
    }

    public async Task<AuthorDeletionOutcome> DeleteAuthorAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var author = await dbContext.Authors.FindAsync([id], cancellationToken);
        if (author == null)
        {
            return AuthorDeletionOutcome.NotFound("Không tìm thấy thông tin tác giả cần xóa.");
        }

        // Kiểm tra xem tác giả đã được liên kết với bất kỳ cuốn sách nào chưa
        var hasBooks = await HasLinkedBooksAsync(id, cancellationToken);
        if (hasBooks)
        {
            logger.LogWarning("Từ chối xóa tác giả #{Id} '{Name}': Tác giả đã có sách liên kết.", author.Id, author.Name);
            return AuthorDeletionOutcome.HasBooks("Không thể xóa tác giả này vì đã có sách liên kết.");
        }

        try
        {
            dbContext.Authors.Remove(author);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Đã xóa tác giả #{Id} '{Name}' thành công.", author.Id, author.Name);
            return AuthorDeletionOutcome.Success("Xóa tác giả thành công.");
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Lỗi khóa ngoại / ràng buộc CSDL khi xóa tác giả #{Id}.", id);
            return AuthorDeletionOutcome.HasBooks("Không thể xóa tác giả này vì đã có sách liên kết.");
        }
    }

    public async Task<Author?> GetAuthorByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await dbContext.Authors.FindAsync([id], cancellationToken);
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

    public async Task<bool> HasLinkedBooksAsync(int authorId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Books.AnyAsync(b => b.AuthorId == authorId, cancellationToken)
            || await dbContext.BookAuthors.AnyAsync(link => link.AuthorId == authorId, cancellationToken);
    }
}
