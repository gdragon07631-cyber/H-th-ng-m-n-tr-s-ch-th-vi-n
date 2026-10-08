namespace Project.Models;

public static class BookCoverPresentation
{
    public const string DefaultCoverPath = "/images/default-book-cover.svg";

    public static string ForList(Book book) =>
        !string.IsNullOrWhiteSpace(book.ThumbnailImagePath) ? book.ThumbnailImagePath :
        !string.IsNullOrWhiteSpace(book.CoverImagePath) ? book.CoverImagePath :
        DefaultCoverPath;

    public static string ForDetails(BookDetailsViewModel book) =>
        string.IsNullOrWhiteSpace(book.CoverImagePath) ? DefaultCoverPath : book.CoverImagePath;
}
