using Project.Models;

namespace Project.Tests;

public sealed class BookCoverPresentationTests
{
    [Fact]
    public void List_uses_thumbnail_belonging_to_book_with_uploaded_cover()
    {
        var book = new Book
        {
            Id = 21,
            CoverImagePath = "/uploads/book-covers/21-a.jpg",
            ThumbnailImagePath = "/uploads/book-thumbnails/21-a.jpg"
        };

        Assert.Equal("/uploads/book-thumbnails/21-a.jpg", BookCoverPresentation.ForList(book));
        Assert.Equal("/uploads/book-covers/21-a.jpg", BookCoverPresentation.ForDetails(new BookDetailsViewModel { CoverImagePath = book.CoverImagePath }));
    }

    [Fact]
    public void List_and_details_use_default_when_book_has_no_cover()
    {
        var book = new Book { Id = 22 };

        Assert.Equal(BookCoverPresentation.DefaultCoverPath, BookCoverPresentation.ForList(book));
        Assert.Equal(BookCoverPresentation.DefaultCoverPath, BookCoverPresentation.ForDetails(new BookDetailsViewModel()));
    }

    [Fact]
    public void List_prefers_uploaded_cover_over_default_when_thumbnail_is_not_yet_available()
    {
        var book = new Book { Id = 23, CoverImagePath = "/uploads/book-covers/23-b.png" };

        Assert.Equal("/uploads/book-covers/23-b.png", BookCoverPresentation.ForList(book));
    }

    [Fact]
    public void Mixed_list_keeps_uploaded_thumbnail_and_default_separate_by_book()
    {
        var books = new[]
        {
            new Book { Id = 31, CoverImagePath = "/uploads/book-covers/31-c.jpg", ThumbnailImagePath = "/uploads/book-thumbnails/31-c.jpg" },
            new Book { Id = 32 }
        };

        Assert.Equal("/uploads/book-thumbnails/31-c.jpg", BookCoverPresentation.ForList(books[0]));
        Assert.Equal(BookCoverPresentation.DefaultCoverPath, BookCoverPresentation.ForList(books[1]));
    }
}
