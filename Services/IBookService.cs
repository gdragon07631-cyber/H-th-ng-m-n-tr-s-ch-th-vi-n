using Project.Models;

namespace Project.Services;

public sealed class BookCreationOutcome
{
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }
    public Book? Book { get; }
    public bool IsDuplicateIsbn { get; }

    /// <summary>Đầu sách đã có cùng nhan đề; chưa lưu, chờ thủ thư xác nhận tiếp tục hoặc hủy.</summary>
    public IReadOnlyList<DuplicateTitleMatch> DuplicateTitleMatches { get; }
    public bool RequiresTitleConfirmation => DuplicateTitleMatches.Count > 0;

    private BookCreationOutcome(bool isSuccess, string? errorMessage, Book? book,
        bool isDuplicateIsbn = false, IReadOnlyList<DuplicateTitleMatch>? duplicateTitleMatches = null)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        Book = book;
        IsDuplicateIsbn = isDuplicateIsbn;
        DuplicateTitleMatches = duplicateTitleMatches ?? [];
    }

    public static BookCreationOutcome Success(Book book) => new(true, null, book);
    public static BookCreationOutcome Failed(string message) => new(false, message, null);
    public static BookCreationOutcome DuplicateIsbn() => new(false, CatalogBookRules.DuplicateIsbnMessage, null, isDuplicateIsbn: true);
    public static BookCreationOutcome DuplicateTitle(IReadOnlyList<DuplicateTitleMatch> matches) =>
        new(false, CatalogBookRules.DuplicateTitleMessage, null, duplicateTitleMatches: matches);
}

public interface IBookService
{
    Task<BookCreationOutcome> CatalogBookAsync(CatalogBookViewModel model, CancellationToken cancellationToken = default);
    Task<bool> IsbnExistsAsync(string? isbn, int? excludeBookId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DuplicateTitleMatch>> FindDuplicateTitlesAsync(string? title, CancellationToken cancellationToken = default);
    Task<CatalogBookViewModel?> GetBookForEditAsync(int id, CancellationToken cancellationToken = default);
    Task<BookCreationOutcome> UpdateBookAsync(int id, CatalogBookViewModel model, CancellationToken cancellationToken = default);
    Task<BookDetailsViewModel?> GetBookDetailsAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Book>> GetAllBooksAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<int, int>> GetCopyCountsAsync(IEnumerable<int> bookIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PublicCatalogBook>> SearchPublicCatalogAsync(string? keyword, CancellationToken cancellationToken = default);
}
