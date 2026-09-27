using Project.Models;

namespace Project.Services;

public sealed class BookCreationOutcome
{
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }
    public Book? Book { get; }

    private BookCreationOutcome(bool isSuccess, string? errorMessage, Book? book)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        Book = book;
    }

    public static BookCreationOutcome Success(Book book) => new(true, null, book);
    public static BookCreationOutcome Failed(string message) => new(false, message, null);
}

public interface IBookService
{
    Task<BookCreationOutcome> CatalogBookAsync(CatalogBookViewModel model, CancellationToken cancellationToken = default);
    Task<BookDetailsViewModel?> GetBookDetailsAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Book>> GetAllBooksAsync(CancellationToken cancellationToken = default);
}
