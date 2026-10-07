using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Project.Controllers;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Tests;

/// <summary>Bạn đọc hủy đơn đặt giữ của mình ngay trên trang chi tiết sách.</summary>
public sealed class BookDetailsHoldCancelTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly ApplicationDbContext db;
    private readonly ReaderRegistrationService registration;
    private readonly EphemeralDataProtectionProvider protector = new();

    public BookDetailsHoldCancelTests()
    {
        connection.Open();
        db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        registration = new ReaderRegistrationService(db, new PasswordHasher<ReaderAccount>(), NullLogger<ReaderRegistrationService>.Instance);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    [Fact]
    public async Task ReaderCancelsWaitingHoldFromBookDetailsPage()
    {
        var reader = await AddReaderAsync("owner@example.com", "R-1");
        var book = await AddBookAsync();
        var hold = await AddHoldAsync(reader, book, BookHoldStatus.Waiting);
        var controller = Controller(reader);

        var result = Assert.IsType<RedirectToActionResult>(await controller.CancelMyHold(book.Id, hold.Id));

        Assert.Equal(nameof(BookController.Details), result.ActionName);
        Assert.Equal(book.Id, result.RouteValues!["id"]);
        Assert.Equal("Hủy đơn đặt giữ thành công.", controller.TempData["HoldSuccessMessage"]);
        Assert.Equal(BookHoldStatus.Cancelled, (await db.BookHolds.AsNoTracking().SingleAsync()).Status);
        var log = await db.AuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal(AuditActions.CancelHold, log.Action);
        Assert.Equal(reader.Email, log.Actor);
        Assert.Contains($"bạn đọc tự hủy đơn đặt giữ #{hold.Id} \"Lão Hạc\"", log.Target);
    }

    [Fact]
    public async Task HoldThatAlreadyHasABookIsNotCancelled()
    {
        var reader = await AddReaderAsync("owner@example.com", "R-1");
        var book = await AddBookAsync();
        var hold = await AddHoldAsync(reader, book, BookHoldStatus.Available);
        var controller = Controller(reader);

        await controller.CancelMyHold(book.Id, hold.Id);

        Assert.NotNull(controller.TempData["HoldErrorMessage"]);
        Assert.Equal(BookHoldStatus.Available, (await db.BookHolds.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task ReaderCannotCancelAnotherReadersHold()
    {
        var owner = await AddReaderAsync("owner@example.com", "R-1");
        var other = await AddReaderAsync("other@example.com", "R-2");
        var book = await AddBookAsync();
        var hold = await AddHoldAsync(owner, book, BookHoldStatus.Waiting);
        var controller = Controller(other);

        await controller.CancelMyHold(book.Id, hold.Id);

        Assert.Equal("Không tìm thấy đơn đặt giữ.", controller.TempData["HoldErrorMessage"]);
        Assert.Equal(BookHoldStatus.Waiting, (await db.BookHolds.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task SignedOutVisitorIsSentToLogin()
    {
        var reader = await AddReaderAsync("owner@example.com", "R-1");
        var book = await AddBookAsync();
        var hold = await AddHoldAsync(reader, book, BookHoldStatus.Waiting);

        var result = Assert.IsType<RedirectToActionResult>(await Controller(signedIn: null).CancelMyHold(book.Id, hold.Id));

        Assert.Equal("Login", result.ActionName);
        Assert.Equal("ReaderRegistration", result.ControllerName);
        Assert.Equal(BookHoldStatus.Waiting, (await db.BookHolds.AsNoTracking().SingleAsync()).Status);
    }

    private async Task<ReaderAccount> AddReaderAsync(string email, string code)
    {
        var reader = new ReaderAccount
        {
            FullName = "Bạn đọc", DateOfBirth = new DateOnly(2000, 1, 1), Email = email, PhoneNumber = "0912345678",
            StudentOrStaffCode = code, Status = "Đang hoạt động", PasswordHash = "x"
        };
        db.ReaderAccounts.Add(reader);
        await db.SaveChangesAsync();
        return reader;
    }

    private async Task<Book> AddBookAsync()
    {
        var book = new Book { Title = "Lão Hạc", Author = new Author { Name = "Nam Cao" } };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book;
    }

    private async Task<BookHold> AddHoldAsync(ReaderAccount reader, Book book, string status)
    {
        var hold = new BookHold { ReaderAccountId = reader.Id, BookId = book.Id, Status = status, HeldAtUtc = DateTime.UtcNow };
        db.BookHolds.Add(hold);
        await db.SaveChangesAsync();
        return hold;
    }

    private BookController Controller(ReaderAccount? signedIn)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.5");
        if (signedIn is not null)
        {
            var cookieContext = new DefaultHttpContext();
            ReaderSessionCookies.Append(cookieContext, protector, signedIn);
            context.Request.Headers.Cookie = string.Join("; ",
                cookieContext.Response.Headers.SetCookie.Select(header => header!.Split(';')[0]));
        }
        var controller = new BookController(null!, null!, null!, registration, null!, null!,
            new AuditLogService(db, NullLogger<AuditLogService>.Instance), db, protector, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            Url = new FixedUrlHelper()
        };
        controller.TempData = new TempDataDictionary(context, new NoTempDataProvider());
        return controller;
    }

    private sealed class FixedUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();
        public string? Action(UrlActionContext actionContext) => "/Book/" + actionContext.Action;
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => "/";
        public string? RouteUrl(UrlRouteContext routeContext) => "/";
    }

    private sealed class NoTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
