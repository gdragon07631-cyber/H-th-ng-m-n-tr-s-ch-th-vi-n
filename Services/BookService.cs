using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Services;

public sealed class BookService(
    ApplicationDbContext dbContext,
    ILogger<BookService> logger) : IBookService
{
    public async Task<BookCreationOutcome> CatalogBookAsync(
        CatalogBookViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
        {
            return BookCreationOutcome.Failed("Vui lòng nhập tên sách.");
        }

        // Kiểm tra tác giả phía server: bắt buộc tồn tại và phải đang ở trạng thái Hoạt động
        var author = await dbContext.Authors.FindAsync([model.AuthorId], cancellationToken);
        if (author == null)
        {
            logger.LogWarning("Biên mục thất bại: Không tìm thấy tác giả #{AuthorId}", model.AuthorId);
            return BookCreationOutcome.Failed("Không tìm thấy tác giả được chọn.");
        }

        if (author.Status != AuthorStatus.Active)
        {
            logger.LogWarning("Biên mục bị từ chối: Tác giả #{AuthorId} '{Name}' đang ở trạng thái '{Status}'.",
                author.Id, author.Name, author.Status);
            return BookCreationOutcome.Failed("Không thể biên mục sách với tác giả đã ngừng sử dụng.");
        }

        var category = await dbContext.Categories.FindAsync([model.CategoryId], cancellationToken);
        if (category == null)
        {
            return BookCreationOutcome.Failed("Không tìm thấy thể loại được chọn.");
        }
        if (category.Status != CategoryStatus.Active)
        {
            return BookCreationOutcome.Failed("Không thể biên mục sách với thể loại đã ngừng sử dụng.");
        }
        if (category.ParentId is int parentId)
        {
            var activeParent = await dbContext.Categories.AnyAsync(c => c.Id == parentId && c.Status == CategoryStatus.Active && c.ParentId == null, cancellationToken);
            if (!activeParent)
                return BookCreationOutcome.Failed("Không thể biên mục sách với thể loại có danh mục cha không hoạt động hoặc không hợp lệ.");
        }

        var book = new Book
        {
            Title = model.Title.Trim(),
            Isbn = string.IsNullOrWhiteSpace(model.Isbn) ? null : model.Isbn.Trim(),
            AuthorId = author.Id,
            CategoryId = category.Id,
            Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.Books.Add(book);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Biên mục sách thành công: #{BookId} '{Title}' gắn với tác giả '{AuthorName}'",
            book.Id, book.Title, author.Name);

        return BookCreationOutcome.Success(book);
    }

    public async Task<BookDetailsViewModel?> GetBookDetailsAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        // Truy vấn lấy chi tiết sách kèm thông tin Tác giả (kể cả tác giả đã ngừng sử dụng)
        var book = await dbContext.Books
            .Include(b => b.Author)
            .Include(b => b.Category)
                .ThenInclude(c => c!.Parent)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (book == null)
        {
            return null;
        }

        return new BookDetailsViewModel
        {
            Id = book.Id,
            Title = book.Title,
            Isbn = book.Isbn,
            AuthorId = book.AuthorId,
            AuthorName = book.Author?.Name ?? "Không xác định",
            AuthorStatus = book.Author?.Status ?? "Không xác định",
            CategoryName = book.Category == null
                ? "Chưa phân loại"
                : book.Category.Parent == null ? book.Category.Name : $"{book.Category.Parent.Name} > {book.Category.Name}",
            Description = book.Description,
            CoverImagePath = book.CoverImagePath,
            ThumbnailImagePath = book.ThumbnailImagePath,
            CreatedAtUtc = book.CreatedAtUtc
        };
    }

    public async Task<IReadOnlyList<Book>> GetAllBooksAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Books
            .Include(b => b.Author)
            .OrderByDescending(b => b.Id)
            .ToListAsync(cancellationToken);
    }
}
