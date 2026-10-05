using System.ComponentModel.DataAnnotations;
using Project.Services;

namespace Project.Models;

public sealed class CatalogBookViewModel
{
    /// <summary>Id đầu sách khi sửa; bỏ qua khi tạo mới.</summary>
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên sách.")]
    [Display(Name = "Tên sách")]
    [MaxLength(250, ErrorMessage = "Tên sách không được vượt quá 250 ký tự.")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Nhan đề phụ")]
    [MaxLength(250, ErrorMessage = "Nhan đề phụ không được vượt quá 250 ký tự.")]
    public string? Subtitle { get; set; }

    [Display(Name = "Mã ISBN")]
    [RegularExpression(CatalogBookRules.IsbnPattern, ErrorMessage = CatalogBookRules.IsbnErrorMessage)]
    public string? Isbn { get; set; }

    /// <summary>Một tác giả (form/API cũ). Khi có AuthorIds thì AuthorIds được dùng thay cho trường này.</summary>
    [Display(Name = "Tác giả")]
    public int AuthorId { get; set; }

    /// <summary>Danh sách tác giả đã chọn từ danh mục, theo thứ tự chọn; tác giả đầu tiên là tác giả chính.</summary>
    [Display(Name = "Tác giả")]
    [RequiresAuthor]
    public List<int> AuthorIds { get; set; } = [];

    /// <summary>
    /// Nhan đề mà thủ thư đã bấm "Xác nhận tiếp tục" sau cảnh báo trùng. Chỉ có hiệu lực khi trùng với nhan đề đang gửi,
    /// nên nếu thủ thư đổi nhan đề sau khi xác nhận thì nhan đề mới vẫn được kiểm tra lại.
    /// </summary>
    public string? ConfirmedDuplicateTitle { get; set; }

    /// <summary>Đầu sách đã có cùng nhan đề, để hiển thị cảnh báo kèm liên kết.</summary>
    public IReadOnlyList<DuplicateTitleMatch> DuplicateTitleMatches { get; set; } = [];

    /// <summary>Tác giả đang được chọn, để hiển thị lại trên form (kể cả tác giả đã gắn nhưng nay ngừng sử dụng).</summary>
    public IReadOnlyList<Author> SelectedAuthors { get; set; } = [];

    /// <summary>Id tác giả cần lưu: AuthorIds (bỏ trùng, giữ thứ tự) hoặc AuthorId nếu chỉ gửi một tác giả.</summary>
    public IReadOnlyList<int> ResolveAuthorIds()
    {
        var ids = AuthorIds.Distinct().ToList();
        if (ids.Count == 0 && AuthorId != 0) ids.Add(AuthorId);
        return ids;
    }

    [Display(Name = "Nhà xuất bản")]
    [MaxLength(200, ErrorMessage = "Nhà xuất bản không được vượt quá 200 ký tự.")]
    public string? Publisher { get; set; }

    [Display(Name = "Năm xuất bản")]
    [Range(CatalogBookRules.MinPublicationYear, 9999, ErrorMessage = CatalogBookRules.PublicationYearErrorMessage)]
    public int? PublicationYear { get; set; }

    [Display(Name = "Số trang")]
    [Range(1, 100000, ErrorMessage = "Số trang phải là số nguyên dương.")]
    public int? PageCount { get; set; }

    [Display(Name = "Mô tả / Tóm tắt")]
    [MaxLength(500, ErrorMessage = "Mô tả không được vượt quá 500 ký tự.")]
    public string? Description { get; set; }

    public IReadOnlyList<Author> ActiveAuthors { get; set; } = [];

    [Required(ErrorMessage = "Vui lòng chọn thể loại.")]
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn thể loại.")]
    [Display(Name = "Thể loại")]
    public int CategoryId { get; set; }

    public IReadOnlyList<Category> ActiveCategories { get; set; } = [];
}

public static class CatalogBookRules
{
    /// <summary>ISBN chỉ gồm chữ số, đúng 10 hoặc 13 chữ số.</summary>
    public const string IsbnPattern = @"^(\d{10}|\d{13})$";
    public const string IsbnErrorMessage = "ISBN chỉ được gồm chữ số và phải có đúng 10 hoặc 13 chữ số.";
    public const int MinPublicationYear = 1000;
    public const string PublicationYearErrorMessage = "Năm xuất bản phải từ 1000 đến năm hiện tại.";
    public const string DuplicateIsbnMessage = "ISBN này đã tồn tại trong hệ thống. Vui lòng kiểm tra lại.";
    public const string DuplicateTitleMessage = "Nhan đề này đã tồn tại trong hệ thống. Vui lòng kiểm tra đầu sách đã có trước khi tiếp tục.";

    /// <summary>Chuẩn hóa nhan đề để so trùng: bỏ khoảng trắng đầu/cuối, không phân biệt hoa thường.</summary>
    public static string NormalizeTitle(string? title) => (title ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsValidIsbn(string isbn) =>
        System.Text.RegularExpressions.Regex.IsMatch(isbn, IsbnPattern);
}

public static class BookCatalogStatus
{
    /// <summary>Đầu sách có tổng số bản sao bằng 0: chưa xuất hiện trong tra cứu công khai.</summary>
    public const string NoCopies = "Chưa có bản sao";
}

/// <summary>Một đầu sách trong kết quả tra cứu công khai (chỉ đầu sách có ít nhất 1 bản sao).</summary>
public sealed record PublicCatalogBook(
    int Id,
    string Title,
    string? Subtitle,
    IReadOnlyList<string> Authors,
    string CategoryName,
    string? Isbn,
    int? PublicationYear,
    string CoverImageUrl,
    int AvailableCopies,
    int TotalCopies);

public sealed class PublicCatalogSearchViewModel
{
    public string? Keyword { get; set; }
    public IReadOnlyList<PublicCatalogBook> Results { get; set; } = [];
    public IReadOnlyList<PublicCatalogCategory> Categories { get; set; } = [];
    public List<string> SelectedCategories { get; set; } = [];
    public int? FromYear { get; set; }
    public int? ToYear { get; set; }
    public bool AvailableOnly { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalItems { get; set; }
    public int TotalPages => TotalItems == 0 ? 0 : (int)Math.Ceiling(TotalItems / (double)PageSize);
}

public sealed record PublicCatalogCategory(int Id, string Name, string? ParentName);

public sealed record PublicCatalogPage(IReadOnlyList<PublicCatalogBook> Items, int Page, int PageSize, int TotalItems)
{
    public int TotalPages => TotalItems == 0 ? 0 : (int)Math.Ceiling(TotalItems / (double)PageSize);
}

/// <summary>Một đầu sách đã có trùng nhan đề với đầu sách đang nhập.</summary>
public sealed record DuplicateTitleMatch(int Id, string Title, string? Subtitle, string? Isbn, string Authors, int? PublicationYear);

public static class BookAuthorList
{
    /// <summary>Tác giả của đầu sách theo thứ tự đã chọn; sách chưa có bảng liên kết thì dùng tác giả chính.</summary>
    public static IReadOnlyList<Author> For(Book book)
    {
        var authors = book.BookAuthors
            .OrderBy(link => link.SortOrder)
            .Select(link => link.Author)
            .OfType<Author>()
            .ToList();
        if (authors.Count == 0 && book.Author != null) authors.Add(book.Author);
        return authors;
    }
}

/// <summary>Đầu sách phải có ít nhất một tác giả (qua AuthorIds hoặc AuthorId).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RequiresAuthorAttribute : ValidationAttribute
{
    public const string Message = "Vui lòng chọn ít nhất một tác giả.";

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
        validationContext.ObjectInstance is CatalogBookViewModel model && model.ResolveAuthorIds().Count == 0
            ? new ValidationResult(Message, [nameof(CatalogBookViewModel.AuthorIds), nameof(CatalogBookViewModel.AuthorId)])
            : ValidationResult.Success;
}

public sealed class BookDetailsViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? Isbn { get; set; }
    public string? Publisher { get; set; }
    public int? PublicationYear { get; set; }
    public int? PageCount { get; set; }
    public int AuthorId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorStatus { get; set; } = string.Empty;
    public IReadOnlyList<Author> Authors { get; set; } = [];
    public string CategoryName { get; set; } = "Chưa phân loại";
    public string? Description { get; set; }
    public string? CoverImagePath { get; set; }
    public string? ThumbnailImagePath { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool IsReaderSignedIn { get; set; }
    public bool CanHold { get; set; }
    public string? HoldBlockedReason { get; set; }
    public ReaderBookHoldItem? ExistingReaderHold { get; set; }
    public bool IsLibrarian { get; set; }
    public int AvailableCopies { get; set; }
    public int TotalCopies { get; set; }
    /// <summary>Only populated for staff viewing the title details; may be status-filtered.</summary>
    public IReadOnlyList<BookHoldQueueItemViewModel> ActiveHoldQueue { get; set; } = [];
    public string SelectedHoldQueueFilter { get; set; } = BookHoldQueueFilter.All;
}
