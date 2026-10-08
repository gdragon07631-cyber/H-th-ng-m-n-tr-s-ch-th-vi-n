using System.ComponentModel.DataAnnotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Tests;

/// <summary>S2-01 – Lát 1: Thủ thư tạo hồ sơ đầu sách cơ bản.</summary>
public sealed class BookCatalogCreationTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly ApplicationDbContext db;
    private readonly BookService service;
    private Author author = null!;
    private Category category = null!;

    public BookCatalogCreationTests()
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
    public async Task CreatesBookWithAllFieldsAndShowsItInTheList()
    {
        var model = FullModel();

        Assert.Empty(Validate(model));
        var outcome = await service.CatalogBookAsync(model);

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        var listed = Assert.Single(await service.GetAllBooksAsync());
        Assert.Equal(outcome.Book!.Id, listed.Id);
        Assert.Equal("Lập trình C#", listed.Title);
        Assert.Equal("Từ cơ bản đến nâng cao", listed.Subtitle);
        Assert.Equal("9786042000000", listed.Isbn);
        Assert.Equal(author.Id, listed.AuthorId);
        Assert.Equal("NXB Trẻ", listed.Publisher);
        Assert.Equal(2024, listed.PublicationYear);
        Assert.Equal(category.Id, listed.CategoryId);
        Assert.Equal("Văn học", listed.Category?.Name);
        Assert.Equal(320, listed.PageCount);
        Assert.Equal("Giới thiệu ngôn ngữ C#.", listed.Description);
    }

    [Theory]
    [InlineData(nameof(CatalogBookViewModel.Title))]
    [InlineData(nameof(CatalogBookViewModel.AuthorId))]
    [InlineData(nameof(CatalogBookViewModel.CategoryId))]
    public async Task MissingRequiredFieldIsRejected(string field)
    {
        var model = FullModel();
        switch (field)
        {
            case nameof(CatalogBookViewModel.Title): model.Title = ""; break;
            case nameof(CatalogBookViewModel.AuthorId): model.AuthorId = 0; break;
            case nameof(CatalogBookViewModel.CategoryId): model.CategoryId = 0; break;
        }

        Assert.Contains(Validate(model), result => result.MemberNames.Contains(field));
        Assert.False((await service.CatalogBookAsync(model)).IsSuccess);
        Assert.Empty(await db.Books.ToListAsync());
    }

    [Theory]
    [InlineData("0306406152")]
    [InlineData("9780306406157")]
    public async Task IsbnWithTenOrThirteenDigitsIsAccepted(string isbn)
    {
        var model = FullModel();
        model.Isbn = isbn;

        Assert.Empty(Validate(model));
        var outcome = await service.CatalogBookAsync(model);

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.Equal(isbn, (await db.Books.SingleAsync()).Isbn);
    }

    [Fact]
    public async Task IsbnIsOptional()
    {
        var model = FullModel();
        model.Isbn = "  ";

        var outcome = await service.CatalogBookAsync(model);

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.Null((await db.Books.SingleAsync()).Isbn);
    }

    [Theory]
    [InlineData("978-604-2-00000")]
    [InlineData("97860420000AB")]
    [InlineData("030640615X")]
    [InlineData("9786042 00000")]
    [InlineData("978604200000@")]
    public async Task IsbnWithLettersOrSpecialCharactersIsRejected(string isbn)
    {
        await AssertIsbnRejectedAsync(isbn);
    }

    [Theory]
    [InlineData("123456789")]
    [InlineData("12345678901")]
    [InlineData("123456789012")]
    [InlineData("12345678901234")]
    public async Task IsbnWithWrongNumberOfDigitsIsRejected(string isbn)
    {
        await AssertIsbnRejectedAsync(isbn);
    }

    private async Task AssertIsbnRejectedAsync(string isbn)
    {
        var model = FullModel();
        model.Isbn = isbn;

        Assert.Contains(Validate(model), result => result.MemberNames.Contains(nameof(CatalogBookViewModel.Isbn)));
        var outcome = await service.CatalogBookAsync(model);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(CatalogBookRules.IsbnErrorMessage, outcome.ErrorMessage);
        Assert.Empty(await db.Books.ToListAsync());
    }

    private CatalogBookViewModel FullModel() => new()
    {
        Title = "Lập trình C#",
        Subtitle = "Từ cơ bản đến nâng cao",
        Isbn = "9786042000000",
        AuthorId = author.Id,
        Publisher = "NXB Trẻ",
        PublicationYear = 2024,
        CategoryId = category.Id,
        PageCount = 320,
        Description = "Giới thiệu ngôn ngữ C#."
    };

    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    private async Task SeedAsync()
    {
        author = new Author { Name = "Nguyễn Văn A", Status = AuthorStatus.Active };
        category = new Category { Name = "Văn học", Status = CategoryStatus.Active };
        db.AddRange(author, category);
        await db.SaveChangesAsync();
    }
}
