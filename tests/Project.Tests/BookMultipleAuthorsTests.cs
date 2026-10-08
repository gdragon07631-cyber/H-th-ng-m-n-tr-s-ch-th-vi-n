using System.ComponentModel.DataAnnotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Tests;

/// <summary>S2-01 – Lát 2: Gắn nhiều tác giả từ danh mục cho một đầu sách.</summary>
public sealed class BookMultipleAuthorsTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly ApplicationDbContext db;
    private readonly BookService service;
    private Author authorA = null!;
    private Author authorB = null!;
    private Author authorC = null!;
    private Author inactiveAuthor = null!;
    private Category category = null!;

    public BookMultipleAuthorsTests()
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

    [Fact]
    public async Task SavesBookWithOneSelectedAuthor()
    {
        var outcome = await service.CatalogBookAsync(Model(authorA.Id));

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.Equal([authorA.Id], await LinkedAuthorIdsAsync(outcome.Book!.Id));
        Assert.Equal(authorA.Id, (await db.Books.SingleAsync()).AuthorId);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task SavesAllSelectedAuthorsInChosenOrder(int count)
    {
        var ids = new[] { authorC.Id, authorA.Id, authorB.Id }.Take(count).ToArray();

        var outcome = await service.CatalogBookAsync(Model(ids));

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.Equal(ids, await LinkedAuthorIdsAsync(outcome.Book!.Id));
        Assert.Equal(ids[0], (await db.Books.SingleAsync()).AuthorId);
    }

    [Fact]
    public async Task RemovingASelectedAuthorBeforeSavingSavesOnlyTheRemainingOnes()
    {
        // Form gửi lên danh sách sau khi thủ thư đã bấm "Bỏ" tác giả B.
        var outcome = await service.CatalogBookAsync(Model(authorA.Id, authorC.Id));

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.Equal([authorA.Id, authorC.Id], await LinkedAuthorIdsAsync(outcome.Book!.Id));
    }

    [Fact]
    public async Task EditingRemovesAnAuthorAndKeepsTheOthers()
    {
        var created = await service.CatalogBookAsync(Model(authorA.Id, authorB.Id, authorC.Id));
        var form = (await service.GetBookForEditAsync(created.Book!.Id))!;
        form.AuthorIds.Remove(authorB.Id);

        var outcome = await service.UpdateBookAsync(form.Id, form);

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.Equal([authorA.Id, authorC.Id], await LinkedAuthorIdsAsync(form.Id));
        Assert.Equal(2, await db.BookAuthors.CountAsync());
    }

    [Fact]
    public async Task EditingCanAddAuthorsAndChangeTheMainAuthor()
    {
        var created = await service.CatalogBookAsync(Model(authorA.Id));
        var form = (await service.GetBookForEditAsync(created.Book!.Id))!;
        form.AuthorIds = [authorB.Id, authorA.Id];

        var outcome = await service.UpdateBookAsync(form.Id, form);

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.Equal([authorB.Id, authorA.Id], await LinkedAuthorIdsAsync(form.Id));
        Assert.Equal(authorB.Id, (await db.Books.AsNoTracking().SingleAsync()).AuthorId);
    }

    [Fact]
    public async Task ReopeningABookShowsTheAuthorsItWasSavedWith()
    {
        var created = await service.CatalogBookAsync(Model(authorB.Id, authorA.Id));

        var form = await service.GetBookForEditAsync(created.Book!.Id);

        Assert.NotNull(form);
        Assert.Equal([authorB.Id, authorA.Id], form.AuthorIds);
        Assert.Equal(["Tác giả B", "Tác giả A"], form.SelectedAuthors.Select(author => author.Name));
        Assert.Equal("Lập trình C#", form.Title);
        Assert.Equal("9786042000000", form.Isbn);
    }

    [Fact]
    public async Task BookListShowsEveryAuthorOfEachBook()
    {
        await service.CatalogBookAsync(Model(authorA.Id));
        var second = Model(authorC.Id, authorB.Id);
        second.Title = "Cấu trúc dữ liệu";
        second.Isbn = "0306406152";
        Assert.True((await service.CatalogBookAsync(second)).IsSuccess);
        db.ChangeTracker.Clear();

        var books = await service.GetAllBooksAsync();

        Assert.Equal(["Tác giả C", "Tác giả B"], BookAuthorList.For(books[0]).Select(author => author.Name));
        Assert.Equal(["Tác giả A"], BookAuthorList.For(books[1]).Select(author => author.Name));
    }

    [Fact]
    public async Task BookCreatedBeforeSlice2StillShowsItsOriginalAuthor()
    {
        db.Books.Add(new Book { Title = "Sách cũ", AuthorId = authorA.Id, CategoryId = category.Id });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var book = Assert.Single(await service.GetAllBooksAsync());

        Assert.Equal(["Tác giả A"], BookAuthorList.For(book).Select(author => author.Name));
    }

    [Fact]
    public async Task InactiveAuthorsAreNotOfferedForSelection()
    {
        var authorService = new AuthorService(db, NullLogger<AuthorService>.Instance);

        var choices = await authorService.GetActiveAuthorsAsync();

        Assert.DoesNotContain(choices, author => author.Id == inactiveAuthor.Id);
        Assert.Equal(3, choices.Count);
    }

    [Fact]
    public async Task InactiveAuthorCannotBeAttachedEvenIfSentDirectly()
    {
        var outcome = await service.CatalogBookAsync(Model(authorA.Id, inactiveAuthor.Id));

        Assert.False(outcome.IsSuccess);
        Assert.Empty(await db.Books.ToListAsync());
        Assert.Empty(await db.BookAuthors.ToListAsync());
    }

    [Theory]
    [InlineData(999)]
    [InlineData(-1)]
    public async Task UnknownAuthorIdCannotBeAttached(int unknownId)
    {
        var outcome = await service.CatalogBookAsync(Model(authorA.Id, unknownId));

        Assert.False(outcome.IsSuccess);
        Assert.Equal("Không tìm thấy tác giả được chọn.", outcome.ErrorMessage);
        Assert.Empty(await db.Books.ToListAsync());
        Assert.Empty(await db.BookAuthors.ToListAsync());
    }

    [Fact]
    public async Task UnknownAuthorIdCannotBeAddedWhenEditing()
    {
        var created = await service.CatalogBookAsync(Model(authorA.Id));
        var form = (await service.GetBookForEditAsync(created.Book!.Id))!;
        form.AuthorIds = [authorA.Id, 999];

        var outcome = await service.UpdateBookAsync(form.Id, form);

        Assert.False(outcome.IsSuccess);
        db.ChangeTracker.Clear();
        Assert.Equal([authorA.Id], await LinkedAuthorIdsAsync(form.Id));
    }

    [Fact]
    public async Task EditingKeepsAnAlreadyAttachedAuthorThatWasLaterDeactivated()
    {
        var created = await service.CatalogBookAsync(Model(authorA.Id, authorB.Id));
        await DeactivateAsync(authorB.Id);
        var form = (await service.GetBookForEditAsync(created.Book!.Id))!;
        form.Title = "Lập trình C# (tái bản)";

        var outcome = await service.UpdateBookAsync(form.Id, form);

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.Equal([authorA.Id, authorB.Id], await LinkedAuthorIdsAsync(form.Id));
    }

    [Fact]
    public async Task DuplicateAuthorIdsAreSavedOnce()
    {
        var outcome = await service.CatalogBookAsync(Model(authorA.Id, authorB.Id, authorA.Id));

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.Equal([authorA.Id, authorB.Id], await LinkedAuthorIdsAsync(outcome.Book!.Id));
    }

    [Fact]
    public async Task AtLeastOneAuthorIsRequired()
    {
        var model = Model();

        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), errors, validateAllProperties: true);

        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(CatalogBookViewModel.AuthorIds)));
        Assert.False((await service.CatalogBookAsync(model)).IsSuccess);
        Assert.Empty(await db.Books.ToListAsync());
    }

    [Fact]
    public async Task Slice1IsbnValidationStillAppliesWithMultipleAuthors()
    {
        var model = Model(authorA.Id, authorB.Id);
        model.Isbn = "978-604-2-00000";

        var outcome = await service.CatalogBookAsync(model);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(CatalogBookRules.IsbnErrorMessage, outcome.ErrorMessage);
    }

    [Fact]
    public async Task CoAuthorCannotBeDeletedFromTheAuthorCatalog()
    {
        await service.CatalogBookAsync(Model(authorA.Id, authorB.Id));
        var authorService = new AuthorService(db, NullLogger<AuthorService>.Instance);

        var outcome = await authorService.DeleteAuthorAsync(authorB.Id);

        Assert.False(outcome.IsSuccess);
        Assert.True(outcome.HasLinkedBooks);
        Assert.True(await db.Authors.AnyAsync(author => author.Id == authorB.Id));
    }

    private CatalogBookViewModel Model(params int[] authorIds) => new()
    {
        Title = "Lập trình C#",
        Isbn = "9786042000000",
        AuthorIds = [.. authorIds],
        CategoryId = category.Id
    };

    private Task<List<int>> LinkedAuthorIdsAsync(int bookId) =>
        db.BookAuthors.AsNoTracking()
            .Where(link => link.BookId == bookId)
            .OrderBy(link => link.SortOrder)
            .Select(link => link.AuthorId)
            .ToListAsync();

    private async Task DeactivateAsync(int authorId)
    {
        var author = await db.Authors.SingleAsync(item => item.Id == authorId);
        author.Status = AuthorStatus.Inactive;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private async Task SeedAsync()
    {
        authorA = new Author { Name = "Tác giả A", Status = AuthorStatus.Active };
        authorB = new Author { Name = "Tác giả B", Status = AuthorStatus.Active };
        authorC = new Author { Name = "Tác giả C", Status = AuthorStatus.Active };
        inactiveAuthor = new Author { Name = "Tác giả ngừng", Status = AuthorStatus.Inactive };
        category = new Category { Name = "Tin học", Status = CategoryStatus.Active };
        db.AddRange(authorA, authorB, authorC, inactiveAuthor, category);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }
}
