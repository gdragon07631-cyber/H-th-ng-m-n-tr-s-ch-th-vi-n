using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Tests;

/// <summary>S2-01 – Lát 3: Chặn ISBN trùng và cảnh báo nhan đề trùng.</summary>
public sealed class BookDuplicateCheckTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly ApplicationDbContext db;
    private readonly BookService service;
    private Author authorA = null!;
    private Author authorB = null!;
    private Author authorC = null!;
    private Category category = null!;

    public BookDuplicateCheckTests()
    {
        connection.Open();
        db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        service = new BookService(db, NullLogger<BookService>.Instance);
        SeedAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    // ---------- ISBN trùng ----------

    [Fact]
    public async Task NewIsbnIsSaved()
    {
        await CreateExistingAsync("Sách cũ", "9786042000000");

        var outcome = await service.CatalogBookAsync(Model("Sách mới", "9780306406157"));

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.Equal(2, await db.Books.CountAsync());
    }

    [Fact]
    public async Task ExistingIsbnIsBlockedAndNothingIsCreated()
    {
        await CreateExistingAsync("Sách cũ", "9786042000000");

        var outcome = await service.CatalogBookAsync(Model("Sách hoàn toàn khác", "9786042000000", authorA.Id, authorB.Id));

        Assert.False(outcome.IsSuccess);
        Assert.True(outcome.IsDuplicateIsbn);
        Assert.Equal("ISBN này đã tồn tại trong hệ thống. Vui lòng kiểm tra lại.", outcome.ErrorMessage);
        Assert.Equal(1, await db.Books.CountAsync());
        Assert.Equal(1, await db.BookAuthors.CountAsync());
    }

    [Fact]
    public async Task ExistingIsbnIsBlockedEvenAfterTitleConfirmation()
    {
        await CreateExistingAsync("Sách cũ", "9786042000000");
        var model = Model("Sách cũ", "9786042000000");
        model.ConfirmedDuplicateTitle = "Sách cũ";

        var outcome = await service.CatalogBookAsync(model);

        Assert.True(outcome.IsDuplicateIsbn);
        Assert.Equal(1, await db.Books.CountAsync());
    }

    [Fact]
    public async Task LegacyIsbnWithHyphensCountsAsTheSameIsbn()
    {
        db.Books.Add(new Book { Title = "Sách nhập trước Lát 1", Isbn = "978-604-2-00000-0", AuthorId = authorA.Id, CategoryId = category.Id });
        await db.SaveChangesAsync();

        var outcome = await service.CatalogBookAsync(Model("Sách mới", "9786042000000"));

        Assert.True(outcome.IsDuplicateIsbn);
    }

    [Fact]
    public async Task BooksWithoutIsbnAreNotTreatedAsDuplicates()
    {
        await CreateExistingAsync("Sách cũ", null);

        var outcome = await service.CatalogBookAsync(Model("Sách mới", null));

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
    }

    [Fact]
    public async Task Slice1IsbnFormatIsStillCheckedBeforeDuplicates()
    {
        await CreateExistingAsync("Sách cũ", "9786042000000");

        var outcome = await service.CatalogBookAsync(Model("Sách mới", "978604200000A"));

        Assert.False(outcome.IsDuplicateIsbn);
        Assert.Equal(CatalogBookRules.IsbnErrorMessage, outcome.ErrorMessage);
    }

    [Fact]
    public async Task EditingCannotChangeIsbnToOneUsedByAnotherBook()
    {
        await CreateExistingAsync("Sách A", "9786042000000");
        var other = await CreateExistingAsync("Sách B", "9780306406157");
        var form = (await service.GetBookForEditAsync(other.Id))!;
        form.Isbn = "9786042000000";

        var outcome = await service.UpdateBookAsync(other.Id, form);

        Assert.True(outcome.IsDuplicateIsbn);
        db.ChangeTracker.Clear();
        Assert.Equal("9780306406157", (await db.Books.SingleAsync(b => b.Id == other.Id)).Isbn);
    }

    [Fact]
    public async Task EditingOtherFieldsKeepsTheBooksOwnIsbn()
    {
        var book = await CreateExistingAsync("Sách A", "9786042000000");
        var form = (await service.GetBookForEditAsync(book.Id))!;
        form.PageCount = 120;

        var outcome = await service.UpdateBookAsync(book.Id, form);

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
    }

    // ---------- Nhan đề trùng ----------

    [Fact]
    public async Task NewTitleIsSavedWithoutWarning()
    {
        await CreateExistingAsync("Sách cũ", null);

        var outcome = await service.CatalogBookAsync(Model("Sách mới", null));

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.False(outcome.RequiresTitleConfirmation);
    }

    [Theory]
    [InlineData("Dế Mèn phiêu lưu ký")]
    [InlineData("  Dế Mèn phiêu lưu ký  ")]
    [InlineData("dế mèn phiêu lưu ký")]
    public async Task ExistingTitleShowsAWarningWithLinkInfoAndSavesNothing(string title)
    {
        var existing = await CreateExistingAsync("Dế Mèn phiêu lưu ký", "9786042000000", authorA.Id, authorB.Id);

        var outcome = await service.CatalogBookAsync(Model(title, "9780306406157"));

        Assert.False(outcome.IsSuccess);
        Assert.True(outcome.RequiresTitleConfirmation);
        var match = Assert.Single(outcome.DuplicateTitleMatches);
        Assert.Equal(existing.Id, match.Id);
        Assert.Equal("Dế Mèn phiêu lưu ký", match.Title);
        Assert.Equal("9786042000000", match.Isbn);
        Assert.Equal("Tác giả A, Tác giả B", match.Authors);
        Assert.Equal(1, await db.Books.CountAsync());
    }

    [Fact]
    public async Task ConfirmingContinuesAndSavesTheNewBookWithAllAuthors()
    {
        await CreateExistingAsync("Dế Mèn phiêu lưu ký", "9786042000000");
        var model = Model("Dế Mèn phiêu lưu ký", "9780306406157", authorC.Id, authorA.Id, authorB.Id);
        model.ConfirmedDuplicateTitle = "Dế Mèn phiêu lưu ký";

        var outcome = await service.CatalogBookAsync(model);

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.Equal(2, await db.Books.CountAsync(b => b.Title == "Dế Mèn phiêu lưu ký"));
        var links = await db.BookAuthors.Where(link => link.BookId == outcome.Book!.Id).OrderBy(link => link.SortOrder).Select(link => link.AuthorId).ToListAsync();
        Assert.Equal([authorC.Id, authorA.Id, authorB.Id], links);
        Assert.Equal(authorC.Id, outcome.Book!.AuthorId);
    }

    [Fact]
    public async Task CancellingAfterTheWarningCreatesNothing()
    {
        await CreateExistingAsync("Dế Mèn phiêu lưu ký", null);

        // Hủy = không gửi xác nhận; lần lưu đầu chỉ trả về cảnh báo và không ghi gì vào database.
        var warning = await service.CatalogBookAsync(Model("Dế Mèn phiêu lưu ký", null));

        Assert.True(warning.RequiresTitleConfirmation);
        Assert.Equal(1, await db.Books.CountAsync());
        Assert.Equal(1, await db.BookAuthors.CountAsync());
    }

    [Fact]
    public async Task ConfirmationOfAnotherTitleDoesNotSkipTheCheck()
    {
        await CreateExistingAsync("Dế Mèn phiêu lưu ký", null);
        await CreateExistingAsync("Tắt đèn", null);
        var model = Model("Tắt đèn", null);
        model.ConfirmedDuplicateTitle = "Dế Mèn phiêu lưu ký";

        var outcome = await service.CatalogBookAsync(model);

        Assert.True(outcome.RequiresTitleConfirmation);
        Assert.Equal(2, await db.Books.CountAsync());
    }

    [Fact]
    public async Task Slice2InactiveOrUnknownAuthorsAreStillRejectedBeforeTheTitleWarning()
    {
        await CreateExistingAsync("Dế Mèn phiêu lưu ký", null);

        var outcome = await service.CatalogBookAsync(Model("Dế Mèn phiêu lưu ký", null, authorA.Id, 999));

        Assert.False(outcome.RequiresTitleConfirmation);
        Assert.Equal("Không tìm thấy tác giả được chọn.", outcome.ErrorMessage);
    }

    private CatalogBookViewModel Model(string title, string? isbn, params int[] authorIds) => new()
    {
        Title = title,
        Isbn = isbn,
        AuthorIds = authorIds.Length == 0 ? [authorA.Id] : [.. authorIds],
        CategoryId = category.Id
    };

    private async Task<Book> CreateExistingAsync(string title, string? isbn, params int[] authorIds)
    {
        var outcome = await service.CatalogBookAsync(Model(title, isbn, authorIds));
        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        return outcome.Book!;
    }

    private async Task SeedAsync()
    {
        authorA = new Author { Name = "Tác giả A", Status = AuthorStatus.Active };
        authorB = new Author { Name = "Tác giả B", Status = AuthorStatus.Active };
        authorC = new Author { Name = "Tác giả C", Status = AuthorStatus.Active };
        category = new Category { Name = "Văn học", Status = CategoryStatus.Active };
        db.AddRange(authorA, authorB, authorC, category);
        await db.SaveChangesAsync();
    }
}
