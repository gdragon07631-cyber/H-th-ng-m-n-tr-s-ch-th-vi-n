using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Project.Controllers;
using Project.Data;
using Project.Models;
using Project.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Project.Tests;

public sealed class BookCoverReplacementTests
{
    [Theory]
    [InlineData(".jpg")]
    [InlineData(".png")]
    public async Task Replacing_cover_updates_original_thumbnail_and_display_paths(string extension)
    {
        using var fixture = new ReplacementFixture();
        var replacement = await CreateImageFileAsync(extension);
        var formFile = new FormFile(new MemoryStream(replacement), 0, replacement.Length, "coverImage", $"replacement{extension}");

        var result = await fixture.Controller.UploadCover(fixture.BookId, formFile);

        Assert.IsType<RedirectToActionResult>(result);
        var savedBook = await fixture.Db.Books.AsNoTracking().SingleAsync(book => book.Id == fixture.BookId);
        Assert.NotEqual(fixture.OldCoverPath, savedBook.CoverImagePath);
        Assert.NotEqual(fixture.OldThumbnailPath, savedBook.ThumbnailImagePath);
        Assert.True(File.Exists(fixture.PhysicalPath(savedBook.CoverImagePath!)));
        Assert.True(File.Exists(fixture.PhysicalPath(savedBook.ThumbnailImagePath!)));
        Assert.Equal(savedBook.ThumbnailImagePath, BookCoverPresentation.ForList(savedBook));
        Assert.Equal(savedBook.CoverImagePath, BookCoverPresentation.ForDetails(new BookDetailsViewModel { CoverImagePath = savedBook.CoverImagePath }));

        using var thumbnail = await Image.LoadAsync(fixture.PhysicalPath(savedBook.ThumbnailImagePath!));
        Assert.Equal(120, thumbnail.Width);
        Assert.Equal(174, thumbnail.Height);
        Assert.False(File.Exists(fixture.PhysicalPath(fixture.OldCoverPath)));
        Assert.False(File.Exists(fixture.PhysicalPath(fixture.OldThumbnailPath)));
    }

    [Fact]
    public async Task Invalid_format_keeps_existing_cover_and_thumbnail()
    {
        using var fixture = new ReplacementFixture();
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var formFile = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "coverImage", "invalid.gif");

        await fixture.Controller.UploadCover(fixture.BookId, formFile);

        await AssertOriginalImagesRemainAsync(fixture);
        Assert.NotNull(fixture.Controller.TempData["CoverUploadError"]);
    }

    [Fact]
    public async Task Oversized_image_keeps_existing_cover_and_thumbnail()
    {
        using var fixture = new ReplacementFixture();
        var bytes = new byte[3 * 1024 * 1024 + 1];
        var formFile = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "coverImage", "too-large.jpg");

        await fixture.Controller.UploadCover(fixture.BookId, formFile);

        await AssertOriginalImagesRemainAsync(fixture);
        Assert.Contains("3MB", Assert.IsType<string>(fixture.Controller.TempData["CoverUploadError"]));
    }

    [Fact]
    public void Book_without_cover_still_uses_default_cover()
    {
        Assert.Equal(BookCoverPresentation.DefaultCoverPath, BookCoverPresentation.ForList(new Book { Id = 500 }));
        Assert.Equal(BookCoverPresentation.DefaultCoverPath, BookCoverPresentation.ForDetails(new BookDetailsViewModel()));
    }

    private static async Task AssertOriginalImagesRemainAsync(ReplacementFixture fixture)
    {
        var savedBook = await fixture.Db.Books.AsNoTracking().SingleAsync(book => book.Id == fixture.BookId);
        Assert.Equal(fixture.OldCoverPath, savedBook.CoverImagePath);
        Assert.Equal(fixture.OldThumbnailPath, savedBook.ThumbnailImagePath);
        Assert.True(File.Exists(fixture.PhysicalPath(fixture.OldCoverPath)));
        Assert.True(File.Exists(fixture.PhysicalPath(fixture.OldThumbnailPath)));
    }

    private static async Task<byte[]> CreateImageFileAsync(string extension)
    {
        await using var stream = new MemoryStream();
        using var image = new Image<Rgba32>(240, 360);
        if (extension == ".jpg") await image.SaveAsJpegAsync(stream);
        else await image.SaveAsPngAsync(stream);
        return stream.ToArray();
    }

    private sealed class ReplacementFixture : IDisposable
    {
        private readonly SqliteConnection connection = new("DataSource=:memory:");
        private const string RefreshCookie = "cover-replacement-test-token";

        public ApplicationDbContext Db { get; }
        public int BookId { get; }
        public string OldCoverPath { get; }
        public string OldThumbnailPath { get; }
        public BookController Controller { get; }

        public ReplacementFixture()
        {
            connection.Open();
            Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            Db.Database.EnsureCreated();

            var author = new Author { Name = $"Cover test author {Guid.NewGuid():N}" };
            Db.Authors.Add(author);
            var librarian = new AdminAccount
            {
                Email = $"{Guid.NewGuid():N}@example.test",
                PasswordHash = "test-hash",
                Role = AccountRoles.Librarian,
                IsActive = true
            };
            Db.AdminAccounts.Add(librarian);
            Db.SaveChanges();
            Db.RefreshTokens.Add(new RefreshToken
            {
                AdminAccountId = librarian.Id,
                TokenHash = TokenService.HashRefreshToken(RefreshCookie),
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
            });
            var book = new Book { Title = "Book to replace cover", AuthorId = author.Id };
            Db.Books.Add(book);
            Db.SaveChanges();
            BookId = book.Id;

            var oldFileName = $"{BookId}-{Guid.NewGuid():N}.jpg";
            OldCoverPath = $"/uploads/book-covers/{oldFileName}";
            OldThumbnailPath = $"/uploads/book-thumbnails/{oldFileName}";
            Directory.CreateDirectory(Path.GetDirectoryName(PhysicalPath(OldCoverPath))!);
            Directory.CreateDirectory(Path.GetDirectoryName(PhysicalPath(OldThumbnailPath))!);
            File.WriteAllBytes(PhysicalPath(OldCoverPath), [1, 2, 3]);
            File.WriteAllBytes(PhysicalPath(OldThumbnailPath), [4, 5, 6]);
            book.CoverImagePath = OldCoverPath;
            book.ThumbnailImagePath = OldThumbnailPath;
            Db.SaveChanges();

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers.Cookie = $"admin_refresh={RefreshCookie}";
            Controller = new BookController(null!, null!, null!, null!, null!, null!, null!, Db, null!, new BookCoverThumbnailService())
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext },
                TempData = new TempDataDictionary(httpContext, new NoOpTempDataProvider())
            };
        }

        public string PhysicalPath(string relativePath) => Path.Combine(
            FrontendPaths.WebRoot(Directory.GetCurrentDirectory()), relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

        public void Dispose()
        {
            var current = Db.Books.AsNoTracking().SingleOrDefault(book => book.Id == BookId);
            if (current?.CoverImagePath != null) TryDelete(PhysicalPath(current.CoverImagePath));
            if (current?.ThumbnailImagePath != null) TryDelete(PhysicalPath(current.ThumbnailImagePath));
            TryDelete(PhysicalPath(OldCoverPath));
            TryDelete(PhysicalPath(OldThumbnailPath));
            Db.Dispose();
            connection.Dispose();
        }

        private static void TryDelete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class NoOpTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
