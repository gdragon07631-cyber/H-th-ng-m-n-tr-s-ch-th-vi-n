using Project.Models;

namespace Project.Services;

public sealed class AuthorCreationOutcome
{
    public bool IsSuccess { get; }
    public bool IsDuplicate { get; }
    public string? ErrorMessage { get; }
    public Author? Author { get; }

    private AuthorCreationOutcome(bool isSuccess, bool isDuplicate, string? errorMessage, Author? author)
    {
        IsSuccess = isSuccess;
        IsDuplicate = isDuplicate;
        ErrorMessage = errorMessage;
        Author = author;
    }

    public static AuthorCreationOutcome Success(Author author) =>
        new(true, false, null, author);

    public static AuthorCreationOutcome Duplicate(string message = "Tên tác giả đã tồn tại.") =>
        new(false, true, message, null);

    public static AuthorCreationOutcome Failed(string message) =>
        new(false, false, message, null);
}

public sealed class AuthorUpdateOutcome
{
    public bool IsSuccess { get; }
    public bool IsDuplicate { get; }
    public bool IsNotFound { get; }
    public string? ErrorMessage { get; }
    public Author? Author { get; }

    private AuthorUpdateOutcome(bool isSuccess, bool isDuplicate, bool isNotFound, string? errorMessage, Author? author)
    {
        IsSuccess = isSuccess;
        IsDuplicate = isDuplicate;
        IsNotFound = isNotFound;
        ErrorMessage = errorMessage;
        Author = author;
    }

    public static AuthorUpdateOutcome Success(Author author) =>
        new(true, false, false, null, author);

    public static AuthorUpdateOutcome Duplicate(string message = "Tên tác giả đã tồn tại.") =>
        new(false, true, false, message, null);

    public static AuthorUpdateOutcome NotFound(string message = "Không tìm thấy tác giả.") =>
        new(false, false, true, message, null);

    public static AuthorUpdateOutcome Failed(string message) =>
        new(false, false, false, message, null);
}

public sealed class AuthorStatusOutcome
{
    public bool IsSuccess { get; }
    public bool IsNotFound { get; }
    public string? ErrorMessage { get; }
    public Author? Author { get; }

    private AuthorStatusOutcome(bool isSuccess, bool isNotFound, string? errorMessage, Author? author)
    {
        IsSuccess = isSuccess;
        IsNotFound = isNotFound;
        ErrorMessage = errorMessage;
        Author = author;
    }

    public static AuthorStatusOutcome Success(Author author) =>
        new(true, false, null, author);

    public static AuthorStatusOutcome NotFound(string message = "Không tìm thấy tác giả.") =>
        new(false, true, message, null);

    public static AuthorStatusOutcome Failed(string message) =>
        new(false, false, message, null);
}

public sealed class AuthorDeletionOutcome
{
    public bool IsSuccess { get; }
    public bool IsNotFound { get; }
    public bool HasLinkedBooks { get; }
    public string? ErrorMessage { get; }

    private AuthorDeletionOutcome(bool isSuccess, bool isNotFound, bool hasLinkedBooks, string? errorMessage)
    {
        IsSuccess = isSuccess;
        IsNotFound = isNotFound;
        HasLinkedBooks = hasLinkedBooks;
        ErrorMessage = errorMessage;
    }

    public static AuthorDeletionOutcome Success(string message = "Xóa tác giả thành công.") =>
        new(true, false, false, message);

    public static AuthorDeletionOutcome NotFound(string message = "Không tìm thấy tác giả.") =>
        new(false, true, false, message);

    public static AuthorDeletionOutcome HasBooks(string message = "Không thể xóa tác giả này vì đã có sách liên kết.") =>
        new(false, false, true, message);

    public static AuthorDeletionOutcome Failed(string message) =>
        new(false, false, false, message);
}

public interface IAuthorService
{
    Task<AuthorCreationOutcome> CreateAuthorAsync(string name, string? note, CancellationToken cancellationToken = default);
    Task<AuthorUpdateOutcome> UpdateAuthorAsync(int id, string name, string? note, CancellationToken cancellationToken = default);
    Task<AuthorStatusOutcome> ToggleAuthorStatusAsync(int id, CancellationToken cancellationToken = default);
    Task<AuthorStatusOutcome> SetAuthorStatusAsync(int id, string status, CancellationToken cancellationToken = default);
    Task<AuthorDeletionOutcome> DeleteAuthorAsync(int id, CancellationToken cancellationToken = default);
    Task<Author?> GetAuthorByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Author>> GetAllAuthorsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Author>> GetActiveAuthorsAsync(CancellationToken cancellationToken = default);
    Task<bool> HasLinkedBooksAsync(int authorId, CancellationToken cancellationToken = default);
}
