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
        var fieldError = ValidateBasicFields(model);
        if (fieldError != null)
        {
            return BookCreationOutcome.Failed(fieldError);
        }

        // Kiểm tra tác giả phía server: tất cả phải tồn tại trong danh mục và đang ở trạng thái Hoạt động
        var authorIds = model.ResolveAuthorIds();
        var authorError = await ValidateAuthorsAsync(authorIds, new HashSet<int>(), cancellationToken);
        if (authorError != null)
        {
            return BookCreationOutcome.Failed(authorError);
        }

        var categoryError = await ValidateCategoryAsync(model.CategoryId, null, cancellationToken);
        if (categoryError != null)
        {
            return BookCreationOutcome.Failed(categoryError);
        }

        // ISBN trùng thì chặn lưu; nhan đề trùng chỉ cảnh báo cho tới khi thủ thư xác nhận đúng nhan đề này.
        if (await IsbnExistsAsync(model.Isbn, null, cancellationToken))
        {
            return BookCreationOutcome.DuplicateIsbn();
        }

        var normalizedTitle = CatalogBookRules.NormalizeTitle(model.Title);
        if (CatalogBookRules.NormalizeTitle(model.ConfirmedDuplicateTitle) != normalizedTitle)
        {
            var matches = await FindDuplicateTitlesAsync(normalizedTitle, cancellationToken);
            if (matches.Count > 0)
            {
                return BookCreationOutcome.DuplicateTitle(matches);
            }
        }

        var book = new Book
        {
            AuthorId = authorIds[0],
            CreatedAtUtc = DateTime.UtcNow
        };
        ApplyBasicFields(book, model);
        for (var index = 0; index < authorIds.Count; index++)
        {
            book.BookAuthors.Add(new BookAuthor { AuthorId = authorIds[index], SortOrder = index });
        }

        dbContext.Books.Add(book);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Biên mục sách thành công: #{BookId} '{Title}' gắn với {AuthorCount} tác giả",
            book.Id, book.Title, authorIds.Count);

        return BookCreationOutcome.Success(book);
    }

    public async Task<CatalogBookViewModel?> GetBookForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var book = await dbContext.Books.AsNoTracking()
            .Include(b => b.Author)
            .Include(b => b.BookAuthors).ThenInclude(link => link.Author)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (book == null)
        {
            return null;
        }

        var authors = BookAuthorList.For(book);
        return new CatalogBookViewModel
        {
            Id = book.Id,
            Title = book.Title,
            Subtitle = book.Subtitle,
            Isbn = book.Isbn,
            Publisher = book.Publisher,
            PublicationYear = book.PublicationYear,
            PageCount = book.PageCount,
            CategoryId = book.CategoryId ?? 0,
            Description = book.Description,
            AuthorIds = authors.Select(author => author.Id).ToList(),
            SelectedAuthors = authors
        };
    }

    public async Task<BookCreationOutcome> UpdateBookAsync(
        int id,
        CatalogBookViewModel model,
        CancellationToken cancellationToken = default)
    {
        var book = await dbContext.Books
            .Include(b => b.BookAuthors)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (book == null)
        {
            return BookCreationOutcome.Failed("Không tìm thấy đầu sách.");
        }

        var fieldError = ValidateBasicFields(model);
        if (fieldError != null)
        {
            return BookCreationOutcome.Failed(fieldError);
        }

        // Tác giả đã gắn từ trước được giữ lại kể cả khi nay đã ngừng sử dụng; tác giả thêm mới phải đang Hoạt động.
        var authorIds = model.ResolveAuthorIds();
        var linkedAuthorIds = book.BookAuthors.Select(link => link.AuthorId).Append(book.AuthorId).ToHashSet();
        var authorError = await ValidateAuthorsAsync(authorIds, linkedAuthorIds, cancellationToken);
        if (authorError != null)
        {
            return BookCreationOutcome.Failed(authorError);
        }

        var categoryError = await ValidateCategoryAsync(model.CategoryId, book.CategoryId, cancellationToken);
        if (categoryError != null)
        {
            return BookCreationOutcome.Failed(categoryError);
        }

        // Chỉ kiểm tra trùng khi ISBN thực sự đổi, để dữ liệu cũ không chặn việc sửa các trường khác.
        var newIsbn = string.IsNullOrWhiteSpace(model.Isbn) ? null : model.Isbn.Trim();
        if (newIsbn != book.Isbn && await IsbnExistsAsync(newIsbn, book.Id, cancellationToken))
        {
            return BookCreationOutcome.DuplicateIsbn();
        }

        ApplyBasicFields(book, model);
        book.AuthorId = authorIds[0];
        foreach (var link in book.BookAuthors.Where(link => !authorIds.Contains(link.AuthorId)).ToList())
        {
            book.BookAuthors.Remove(link);
        }
        for (var index = 0; index < authorIds.Count; index++)
        {
            var link = book.BookAuthors.FirstOrDefault(item => item.AuthorId == authorIds[index]);
            if (link == null)
            {
                book.BookAuthors.Add(new BookAuthor { BookId = book.Id, AuthorId = authorIds[index], SortOrder = index });
            }
            else
            {
                link.SortOrder = index;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Cập nhật đầu sách #{BookId} '{Title}' với {AuthorCount} tác giả", book.Id, book.Title, authorIds.Count);
        return BookCreationOutcome.Success(book);
    }

    public async Task<bool> IsbnExistsAsync(string? isbn, int? excludeBookId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(isbn))
        {
            return false;
        }

        // ISBN nhập trước Lát 1 có thể chứa dấu gạch/khoảng trắng nên so trên dạng chỉ còn chữ số.
        var digits = isbn.Trim();
        return await dbContext.Books.AnyAsync(
            b => b.Isbn != null && b.Id != excludeBookId && b.Isbn.Replace("-", "").Replace(" ", "") == digits,
            cancellationToken);
    }

    public async Task<IReadOnlyList<DuplicateTitleMatch>> FindDuplicateTitlesAsync(string? title, CancellationToken cancellationToken = default)
    {
        var normalized = CatalogBookRules.NormalizeTitle(title);
        if (normalized.Length == 0)
        {
            return [];
        }

        var books = await dbContext.Books.AsNoTracking()
            .Include(b => b.Author)
            .Include(b => b.BookAuthors).ThenInclude(link => link.Author)
            .Where(b => b.Title.Trim().ToLower() == normalized)
            .OrderBy(b => b.Id)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return books
            .Select(b => new DuplicateTitleMatch(
                b.Id,
                b.Title,
                b.Subtitle,
                b.Isbn,
                string.Join(", ", BookAuthorList.For(b).Select(author => author.Name)),
                b.PublicationYear))
            .ToList();
    }

    private static string? ValidateBasicFields(CatalogBookViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
        {
            return "Vui lòng nhập tên sách.";
        }

        var isbn = string.IsNullOrWhiteSpace(model.Isbn) ? null : model.Isbn.Trim();
        if (isbn != null && !CatalogBookRules.IsValidIsbn(isbn))
        {
            return CatalogBookRules.IsbnErrorMessage;
        }

        if (model.PublicationYear is int year && (year < CatalogBookRules.MinPublicationYear || year > DateTime.Now.Year))
        {
            return CatalogBookRules.PublicationYearErrorMessage;
        }

        if (model.PageCount is <= 0)
        {
            return "Số trang phải là số nguyên dương.";
        }

        return null;
    }

    private static void ApplyBasicFields(Book book, CatalogBookViewModel model)
    {
        book.Title = model.Title.Trim();
        book.Subtitle = string.IsNullOrWhiteSpace(model.Subtitle) ? null : model.Subtitle.Trim();
        book.Isbn = string.IsNullOrWhiteSpace(model.Isbn) ? null : model.Isbn.Trim();
        book.Publisher = string.IsNullOrWhiteSpace(model.Publisher) ? null : model.Publisher.Trim();
        book.PublicationYear = model.PublicationYear;
        book.PageCount = model.PageCount;
        book.CategoryId = model.CategoryId;
        book.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
    }

    private async Task<string?> ValidateAuthorsAsync(
        IReadOnlyList<int> authorIds,
        IReadOnlySet<int> alreadyLinkedAuthorIds,
        CancellationToken cancellationToken)
    {
        if (authorIds.Count == 0)
        {
            return RequiresAuthorAttribute.Message;
        }

        var authors = await dbContext.Authors.AsNoTracking()
            .Where(author => authorIds.Contains(author.Id))
            .ToListAsync(cancellationToken);
        if (authors.Count != authorIds.Count)
        {
            var missing = authorIds.Except(authors.Select(author => author.Id));
            logger.LogWarning("Biên mục thất bại: Không tìm thấy tác giả #{AuthorIds}", string.Join(", ", missing));
            return "Không tìm thấy tác giả được chọn.";
        }

        var inactive = authors.FirstOrDefault(author => author.Status != AuthorStatus.Active && !alreadyLinkedAuthorIds.Contains(author.Id));
        if (inactive != null)
        {
            logger.LogWarning("Biên mục bị từ chối: Tác giả #{AuthorId} '{Name}' đang ở trạng thái '{Status}'.",
                inactive.Id, inactive.Name, inactive.Status);
            return "Không thể biên mục sách với tác giả đã ngừng sử dụng.";
        }

        return null;
    }

    private async Task<string?> ValidateCategoryAsync(int categoryId, int? currentCategoryId, CancellationToken cancellationToken)
    {
        // Thể loại đang gắn với đầu sách được giữ nguyên khi sửa; chọn thể loại khác thì phải đang hoạt động.
        if (currentCategoryId == categoryId && categoryId != 0)
        {
            return null;
        }

        var category = await dbContext.Categories.FindAsync([categoryId], cancellationToken);
        if (category == null)
        {
            return "Không tìm thấy thể loại được chọn.";
        }
        if (category.Status != CategoryStatus.Active)
        {
            return "Không thể biên mục sách với thể loại đã ngừng sử dụng.";
        }
        if (category.ParentId is int parentId)
        {
            var activeParent = await dbContext.Categories.AnyAsync(c => c.Id == parentId && c.Status == CategoryStatus.Active && c.ParentId == null, cancellationToken);
            if (!activeParent)
                return "Không thể biên mục sách với thể loại có danh mục cha không hoạt động hoặc không hợp lệ.";
        }

        return null;
    }

    public async Task<BookDetailsViewModel?> GetBookDetailsAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        // Truy vấn lấy chi tiết sách kèm thông tin Tác giả (kể cả tác giả đã ngừng sử dụng)
        var book = await dbContext.Books
            .Include(b => b.Author)
            .Include(b => b.BookAuthors).ThenInclude(link => link.Author)
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
            Subtitle = book.Subtitle,
            Isbn = book.Isbn,
            Publisher = book.Publisher,
            PublicationYear = book.PublicationYear,
            PageCount = book.PageCount,
            AuthorId = book.AuthorId,
            AuthorName = book.Author?.Name ?? "Không xác định",
            AuthorStatus = book.Author?.Status ?? "Không xác định",
            Authors = BookAuthorList.For(book),
            CategoryName = book.Category == null
                ? "Chưa phân loại"
                : book.Category.Parent == null ? book.Category.Name : $"{book.Category.Parent.Name} > {book.Category.Name}",
            Description = book.Description,
            CoverImagePath = book.CoverImagePath,
            ThumbnailImagePath = book.ThumbnailImagePath,
            CreatedAtUtc = book.CreatedAtUtc,
            // Chỉ bản "Sẵn sàng" là bản rảnh; bản Đang sửa chữa/Đang mượn/Đang giữ không được tính.
            AvailableCopies = await dbContext.BookCopies.CountAsync(
                copy => copy.BookId == book.Id && copy.Status == BookCopyStatus.Available, cancellationToken),
            TotalCopies = await dbContext.BookCopies.CountAsync(copy => copy.BookId == book.Id, cancellationToken)
        };
    }

    public async Task<IReadOnlyDictionary<int, int>> GetCopyCountsAsync(IEnumerable<int> bookIds, CancellationToken cancellationToken = default)
    {
        var ids = bookIds.Distinct().ToList();
        var counts = await dbContext.BookCopies.AsNoTracking()
            .Where(copy => ids.Contains(copy.BookId))
            .GroupBy(copy => copy.BookId)
            .Select(group => new { BookId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.BookId, item => item.Count, cancellationToken);
        return ids.ToDictionary(id => id, id => counts.GetValueOrDefault(id));
    }

    public async Task<IReadOnlyList<PublicCatalogBook>> SearchPublicCatalogAsync(string? keyword, CancellationToken cancellationToken = default)
    {
        // Chỉ đầu sách có ít nhất 1 bản sao mới được công khai; đầu sách "Chưa có bản sao" bị loại khỏi kết quả.
        var query = dbContext.Books.AsNoTracking()
            .Where(b => dbContext.BookCopies.Any(copy => copy.BookId == b.Id));

        var term = keyword?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var lowered = term.ToLower();
            query = query.Where(b =>
                b.Title.ToLower().Contains(lowered)
                || (b.Subtitle != null && b.Subtitle.ToLower().Contains(lowered))
                || (b.Isbn != null && b.Isbn.Contains(term))
                || (b.Author != null && b.Author.Name.ToLower().Contains(lowered))
                || b.BookAuthors.Any(link => link.Author != null && link.Author.Name.ToLower().Contains(lowered)));
        }

        var books = await query
            .Include(b => b.Author)
            .Include(b => b.BookAuthors).ThenInclude(link => link.Author)
            .Include(b => b.Category).ThenInclude(c => c!.Parent)
            .OrderBy(b => b.Title)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var bookIds = books.Select(b => b.Id).ToList();
        var copies = await dbContext.BookCopies.AsNoTracking()
            .Where(copy => bookIds.Contains(copy.BookId))
            .GroupBy(copy => copy.BookId)
            .Select(group => new
            {
                BookId = group.Key,
                Total = group.Count(),
                Available = group.Count(copy => copy.Status == BookCopyStatus.Available)
            })
            .ToDictionaryAsync(item => item.BookId, cancellationToken);

        return books.Select(b => new PublicCatalogBook(
                b.Id,
                b.Title,
                b.Subtitle,
                BookAuthorList.For(b).Select(author => author.Name).ToList(),
                b.Category == null
                    ? "Chưa phân loại"
                    : b.Category.Parent == null ? b.Category.Name : $"{b.Category.Parent.Name} > {b.Category.Name}",
                b.Isbn,
                b.PublicationYear,
                BookCoverPresentation.ForList(b),
                copies[b.Id].Available,
                copies[b.Id].Total))
            .ToList();
    }

    public async Task<IReadOnlyList<Book>> GetAllBooksAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Books
            .Include(b => b.Author)
            .Include(b => b.BookAuthors).ThenInclude(link => link.Author)
            .Include(b => b.Category)
                .ThenInclude(c => c!.Parent)
            .OrderByDescending(b => b.Id)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }
}
