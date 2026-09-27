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

    public static AuthorCreationOutcome Duplicate(string message = "Tác giả với tên này đã tồn tại trong hệ thống.") =>
        new(false, true, message, null);

    public static AuthorCreationOutcome Failed(string message) =>
        new(false, false, message, null);
}

public interface IAuthorService
{
    Task<AuthorCreationOutcome> CreateAuthorAsync(string name, string? note, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Author>> GetAllAuthorsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Author>> GetActiveAuthorsAsync(CancellationToken cancellationToken = default);
}
