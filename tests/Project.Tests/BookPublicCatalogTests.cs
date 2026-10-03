using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Project.Controllers;
using Project.Data;
using Project.Filters;
using Project.Models;
using Project.Services;

namespace Project.Tests;

/// <summary>S2-01 – Lát 4: Trạng thái "Chưa có bản sao" và tra cứu công khai.</summary>
public sealed class BookPublicCatalogTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly ApplicationDbContext db;
    private readonly BookService service;
    private readonly BookCopyService copyService;
    private Author authorA = null!;
    private Author authorB = null!;
    private Author authorC = null!;
    private Category parentCategory = null!;
    private Category category = null!;
    private Shelf shelf = null!;

    public BookPublicCatalogTests()
    {
        connection.Open();
        db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        service = new BookService(db, NullLogger<BookService>.Instance);
        copyService = new BookCopyService(db);
        SeedAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    [Fact]
    public async Task NewBookHasNoCopiesStatusOnLibrarianListAndDetails()
    {
        var book = await CreateBookAsync("Dế Mèn phiêu lưu ký", authorA.Id);

        var counts = await service.GetCopyCountsAsync([book.Id]);
        var details = await service.GetBookDetailsAsync(book.Id);

        Assert.Equal(0, counts[book.Id]);
        Assert.Equal(0, details!.TotalCopies);
    }

    [Fact]
    public async Task BookWithoutCopiesIsHiddenFromPublicSearch()
    {
        await CreateBookAsync("Dế Mèn phiêu lưu ký", authorA.Id);

        Assert.Empty(await service.SearchPublicCatalogAsync(null));
        Assert.Empty(await service.SearchPublicCatalogAsync("Dế Mèn"));
    }

    [Fact]
    public async Task AddingTheFirstCopyMakesTheBookAppearWithTitleAuthorsAndCategory()
    {
        var book = await CreateBookAsync("Dế Mèn phiêu lưu ký", authorA.Id);
        Assert.Empty(await service.SearchPublicCatalogAsync("Dế Mèn"));

        var added = await copyService.AddAsync(book.Id, new NewBookCopyViewModel { CopyCode = "BC-0001", ShelfId = shelf.Id });

        Assert.True(added.IsSuccess, added.ErrorMessage);
        Assert.Equal(1, (await service.GetCopyCountsAsync([book.Id]))[book.Id]);
        var result = Assert.Single(await service.SearchPublicCatalogAsync("Dế Mèn"));
        Assert.Equal(book.Id, result.Id);
        Assert.Equal("Dế Mèn phiêu lưu ký", result.Title);
        Assert.Equal(["Tác giả A"], result.Authors);
        Assert.Equal("Văn học > Thiếu nhi", result.CategoryName);
        Assert.Equal(1, result.TotalCopies);
        Assert.Equal(1, result.AvailableCopies);
    }

    [Fact]
    public async Task BookWithSeveralAuthorsShowsAllOfThemInOrder()
    {
        var book = await CreateBookAsync("Tuyển tập truyện ngắn", authorC.Id, authorA.Id, authorB.Id);
        await AddCopyAsync(book.Id, "BC-0001");

        var result = Assert.Single(await service.SearchPublicCatalogAsync(null));

        Assert.Equal(["Tác giả C", "Tác giả A", "Tác giả B"], result.Authors);
    }

    [Theory]
    [InlineData("tuyển tập")]
    [InlineData("Tác giả B")]
    [InlineData("9786042000000")]
    public async Task SearchMatchesTitleAnyAuthorOrIsbn(string keyword)
    {
        var book = await CreateBookAsync("Tuyển tập truyện ngắn", authorC.Id, authorB.Id);
        await AddCopyAsync(book.Id, "BC-0001");
        var other = await CreateBookAsync("Sách khác", authorA.Id, isbn: "0306406152");
        await AddCopyAsync(other.Id, "BC-0002");

        var result = Assert.Single(await service.SearchPublicCatalogAsync(keyword));

        Assert.Equal(book.Id, result.Id);
    }

    [Fact]
    public async Task OnlyBooksWithCopiesAreListedEvenWhenAllCopiesAreOnLoanOrUnderRepair()
    {
        var withCopy = await CreateBookAsync("Có bản sao", authorA.Id);
        await AddCopyAsync(withCopy.Id, "BC-0001", BookCopyStatus.UnderRepair);
        await CreateBookAsync("Chưa có bản sao", authorA.Id, isbn: "0306406152");

        var result = Assert.Single(await service.SearchPublicCatalogAsync(null));

        Assert.Equal(withCopy.Id, result.Id);
        Assert.Equal(0, result.AvailableCopies);
        Assert.Equal(1, result.TotalCopies);
    }

    [Fact]
    public async Task PublicCatalogPageNeedsNoSignInAndShowsOnlyBooksWithCopies()
    {
        var shown = await CreateBookAsync("Có bản sao", authorA.Id);
        await AddCopyAsync(shown.Id, "BC-0001");
        await CreateBookAsync("Chưa có bản sao", authorB.Id, isbn: "0306406152");
        var controller = new CatalogController(service);

        var view = Assert.IsType<ViewResult>(await controller.Index(null));

        Assert.Empty(typeof(CatalogController).GetCustomAttributes(typeof(StaffOnlyAttribute), true));
        var model = Assert.IsType<PublicCatalogSearchViewModel>(view.Model);
        Assert.Equal([shown.Id], model.Results.Select(book => book.Id));
    }

    private async Task<Book> CreateBookAsync(string title, int authorId, params int[] moreAuthorIds) =>
        await CreateBookAsync(title, [authorId, .. moreAuthorIds], "9786042000000");

    private async Task<Book> CreateBookAsync(string title, int authorId, string isbn) =>
        await CreateBookAsync(title, [authorId], isbn);

    private async Task<Book> CreateBookAsync(string title, List<int> authorIds, string isbn)
    {
        var outcome = await service.CatalogBookAsync(new CatalogBookViewModel
        {
            Title = title,
            Isbn = isbn,
            AuthorIds = authorIds,
            CategoryId = category.Id
        });
        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        return outcome.Book!;
    }

    private async Task AddCopyAsync(int bookId, string code, string status = BookCopyStatus.Available)
    {
        db.BookCopies.Add(new BookCopy { BookId = bookId, ShelfId = shelf.Id, CopyCode = code, Status = status });
        await db.SaveChangesAsync();
    }

    private async Task SeedAsync()
    {
        authorA = new Author { Name = "Tác giả A", Status = AuthorStatus.Active };
        authorB = new Author { Name = "Tác giả B", Status = AuthorStatus.Active };
        authorC = new Author { Name = "Tác giả C", Status = AuthorStatus.Active };
        parentCategory = new Category { Name = "Văn học", Status = CategoryStatus.Active };
        db.AddRange(authorA, authorB, authorC, parentCategory);
        await db.SaveChangesAsync();
        category = new Category { Name = "Thiếu nhi", Status = CategoryStatus.Active, ParentId = parentCategory.Id };
        var warehouse = new Warehouse { Code = "KHO-A", Name = "Kho A" };
        db.AddRange(category, warehouse);
        await db.SaveChangesAsync();
        shelf = new Shelf { Code = "A1", Name = "Kệ A1", WarehouseId = warehouse.Id };
        db.Shelves.Add(shelf);
        await db.SaveChangesAsync();
    }
}
