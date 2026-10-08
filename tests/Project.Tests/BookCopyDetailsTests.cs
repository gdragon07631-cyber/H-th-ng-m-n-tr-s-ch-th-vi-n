using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Project.Controllers;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Tests;

public sealed class BookCopyDetailsTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly ApplicationDbContext db;
    private readonly BookService service;
    private readonly Book book;
    private readonly Book otherBook;
    private readonly Shelf shelf;

    public BookCopyDetailsTests()
    {
        connection.Open();
        db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        var author = new Author { Name = "Tác giả" };
        var warehouse = new Warehouse { Code = "K1", Name = "Kho 1" };
        db.AddRange(author, warehouse);
        db.SaveChanges();
        shelf = new() { Code = "S1", Name = "Kệ 1", WarehouseId = warehouse.Id };
        book = new() { Title = "Đầu sách A", AuthorId = author.Id };
        otherBook = new() { Title = "Đầu sách B", AuthorId = author.Id };
        db.AddRange(shelf, book, otherBook);
        db.SaveChanges();
        service = new(db, NullLogger<BookService>.Instance);
    }

    private async Task<BookCopy> Add(int bookId, string code, string status = BookCopyStatus.Available)
    {
        var copy = new BookCopy
        {
            BookId = bookId, CopyCode = code, ShelfId = shelf.Id, Status = status,
            ReceivedDate = new(2026, 10, 5), CoverPrice = 123456.50m,
            PhysicalCondition = BookCopyCondition.Worn
        };
        db.Add(copy);
        await db.SaveChangesAsync();
        return copy;
    }

    [Fact]
    public async Task EachBookOnlyListsItsOwnCopiesAndCounts()
    {
        var first = await Add(book.Id, "A-001");
        await Add(book.Id, "A-002", BookCopyStatus.OnLoan);
        var other = await Add(otherBook.Id, "B-001");
        var details = (await service.GetBookDetailsAsync(book.Id))!;
        Assert.Equal(new[] { "A-001", "A-002" }, details.Copies.Select(copy => copy.CopyCode));
        Assert.DoesNotContain(details.Copies, copy => copy.Id == other.Id);
        Assert.Contains(details.Copies, copy => copy.Id == first.Id);
        Assert.Equal(2, details.TotalCopies);
        Assert.Equal(1, details.AvailableCopies);
        var otherDetails = (await service.GetBookDetailsAsync(otherBook.Id))!;
        Assert.Equal(other.Id, Assert.Single(otherDetails.Copies).Id);
        Assert.Equal(1, otherDetails.TotalCopies);
        Assert.Equal(1, otherDetails.AvailableCopies);
    }

    [Fact]
    public async Task DetailsExposeAllSevenFieldsAndRenderThemInTable()
    {
        var saved = await Add(book.Id, "BC-123");
        var details = (await service.GetBookDetailsAsync(book.Id))!;
        var item = Assert.Single(details.Copies);
        Assert.Equal(saved.CopyCode, item.CopyCode);
        Assert.Equal("K1 - Kho 1", item.Warehouse);
        Assert.Equal("S1 - Kệ 1", item.Shelf);
        Assert.Equal(saved.ReceivedDate, item.ReceivedDate);
        Assert.Equal(saved.CoverPrice, item.CoverPrice);
        Assert.Equal(saved.PhysicalCondition, item.PhysicalCondition);
        Assert.Equal(saved.Status, item.Status);
        var html = await Render(details);
        foreach (var value in new[] { item.CopyCode, item.Warehouse, item.Shelf, "05/10/2026", "123.456,50 đ", item.PhysicalCondition, "Sẵn sàng: 1", "Tổng số bản sao: 1" })
            Assert.Contains(value, html);
        Assert.Contains($"data-copy-id=\"{saved.Id}\"", html);
    }

    [Fact]
    public async Task AllFiveStatusesAreDisplayedAndOnlyAvailableCopiesCountAsReady()
    {
        foreach (var status in new[] { BookCopyStatus.Available, BookCopyStatus.OnLoan, BookCopyStatus.OnHold, BookCopyStatus.UnderRepair, BookCopyStatus.Removed })
            await Add(book.Id, $"BC-{await db.BookCopies.CountAsync()}", status);
        await Add(book.Id, "EXTRA-READY");
        await Add(otherBook.Id, "OTHER-READY");
        var details = (await service.GetBookDetailsAsync(book.Id))!;
        Assert.Equal(6, details.TotalCopies);
        Assert.Equal(2, details.AvailableCopies);
        var html = await Render(details);
        foreach (var status in new[] { "Sẵn sàng", "Đang mượn", "Đang giữ cho đặt trước", "Đang sửa chữa", "Đã loại khỏi kho" })
            Assert.Contains(status, html);
        Assert.DoesNotContain("OTHER-READY", html);
        Assert.Equal("Đang giữ", details.Copies.Single(copy => copy.Status == BookCopyStatus.OnHold).Status);
    }

    [Fact]
    public async Task AddingSliceOneCopyRefreshesListTotalAndReadyCount()
    {
        await Add(book.Id, "ON-LOAN", BookCopyStatus.OnLoan);
        var before = (await service.GetBookDetailsAsync(book.Id))!;
        var result = await new BookCopyService(db).AddManualAsync(book.Id, new()
        {
            CopyCode = "NEW-MANUAL", WarehouseId = shelf.WarehouseId, ShelfId = shelf.Id,
            ReceivedDate = new(2026, 10, 5), CoverPrice = 10000,
            PhysicalCondition = BookCopyCondition.Good
        });
        Assert.True(result.IsSuccess);
        var after = (await service.GetBookDetailsAsync(book.Id))!;
        Assert.Equal(before.TotalCopies + 1, after.TotalCopies);
        Assert.Equal(before.AvailableCopies + 1, after.AvailableCopies);
        Assert.Contains(after.Copies, copy => copy.Id == result.Copy!.Id && copy.CopyCode == "NEW-MANUAL");
    }

    [Fact]
    public async Task LegacyCopyWithMissingDateAndPriceRemainsVisible()
    {
        var copy = await Add(book.Id, "LEGACY");
        copy.ReceivedDate = null;
        copy.CoverPrice = null;
        await db.SaveChangesAsync();
        var details = (await service.GetBookDetailsAsync(book.Id))!;
        var item = Assert.Single(details.Copies);
        Assert.Equal("Chưa có", item.ReceivedDateText);
        Assert.Equal("Chưa có", item.CoverPriceText);
        Assert.Contains("LEGACY", await Render(details));
    }

    [Fact]
    public async Task EmptyBookShowsZeroCountsAndEmptyMessage()
    {
        var details = (await service.GetBookDetailsAsync(book.Id))!;
        Assert.Empty(details.Copies);
        Assert.Equal(0, details.TotalCopies);
        Assert.Equal(0, details.AvailableCopies);
        var html = await Render(details);
        Assert.Contains("Đầu sách chưa có bản sao.", html);
        Assert.Contains("Tổng số bản sao: 0", html);
        Assert.Contains("Sẵn sàng: 0", html);
    }

    [Fact]
    public async Task ExistingCopyCannotMoveToAnotherTitle()
    {
        var copy = await Add(book.Id, "FIXED-TITLE");
        var result = await new BookCopyService(db).UpdateAsync(copy.Id, new()
        {
            BookId = otherBook.Id, WarehouseId = shelf.WarehouseId, ShelfId = shelf.Id,
            PhysicalCondition = BookCopyCondition.Good, Status = BookCopyStatus.Available
        }, "staff");
        Assert.True(result.IsSuccess);
        Assert.Equal(copy.Id, Assert.Single((await service.GetBookDetailsAsync(book.Id))!.Copies).Id);
        Assert.Empty((await service.GetBookDetailsAsync(otherBook.Id))!.Copies);
    }

    private static async Task<string> Render(BookDetailsViewModel model)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(BookController).Assembly.GetName().Name,
            ContentRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")),
            EnvironmentName = "Development"
        });
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(BookController).Assembly);
        await using var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var action = new ActionContext(new DefaultHttpContext { RequestServices = services }, new RouteData(), new ActionDescriptor());
        var view = services.GetRequiredService<ICompositeViewEngine>().GetView(null, "/Views/Book/_Copies.cshtml", false);
        Assert.True(view.Success, string.Join(", ", view.SearchedLocations ?? []));
        using var writer = new StringWriter();
        var context = new ViewContext(action, view.View!,
            new ViewDataDictionary<BookDetailsViewModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model },
            new TempDataDictionary(action.HttpContext, services.GetRequiredService<ITempDataProvider>()), writer, new HtmlHelperOptions());
        await view.View!.RenderAsync(context);
        return WebUtility.HtmlDecode(writer.ToString());
    }

    public void Dispose() { db.Dispose(); connection.Dispose(); }
}
