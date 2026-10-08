using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Project.Controllers;
using Project.Data;
using Project.Models;
using Project.Services;
using Xunit;

namespace Project.Tests;

public class LoanCardLimitTests
{
    private static (ApplicationDbContext Db, BookLoanService Service) CreateTestEnvironment()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(options);
        var calendar = new WorkingScheduleService(db);
        var service = new BookLoanService(db, calendar);
        return (db, service);
    }

    private static async Task<(ReaderAccount Reader, List<Book> Books)> SeedReaderAndBooksAsync(
        ApplicationDbContext db, int maxBooks = 5, int existingLoanCount = 0)
    {
        var cardType = new LibraryCardType
        {
            Name = "Thẻ sinh viên",
            MaxBooks = maxBooks
        };
        db.LibraryCardTypes.Add(cardType);

        var reader = new ReaderAccount
        {
            FullName = "Nguyễn Văn Kiểm Thử",
            DateOfBirth = new DateOnly(2000, 1, 1),
            Email = $"reader-{Guid.NewGuid():N}@example.test",
            PhoneNumber = "0912345678",
            StudentOrStaffCode = $"SV-{Guid.NewGuid():N}"[..8],
            PasswordHash = "hash",
            Status = "Đang hoạt động",
            LibraryCard = new LibraryCard
            {
                CardCode = $"CARD-{Guid.NewGuid():N}"[..10],
                LibraryCardType = cardType,
                IssuedOn = DateOnly.FromDateTime(DateTime.Today),
                ExpiresOn = DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
                Status = "Đang hoạt động"
            }
        };
        db.ReaderAccounts.Add(reader);

        var books = new List<Book>();
        for (var i = 0; i < 10; i++)
        {
            var book = new Book
            {
                Title = $"Sách kiểm thử #{i + 1}",
                Author = new Author { Name = $"Tác giả #{i + 1}" }
            };
            db.Books.Add(book);
            books.Add(book);
        }

        await db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.Today);
        for (var i = 0; i < existingLoanCount; i++)
        {
            db.BookLoans.Add(new BookLoan
            {
                BookId = books[i].Id,
                ReaderAccountId = reader.Id,
                LoanDate = today.AddDays(-i - 1),
                OriginalDueDate = today.AddDays(14 - i),
                DueDate = today.AddDays(14 - i)
            });
        }
        await db.SaveChangesAsync();

        return (reader, books);
    }

    [Fact]
    public async Task Reader_WithZeroLoans_AndLimitFive_CanBorrow()
    {
        // Bạn đọc đang mượn 0 sách và hạn mức là 5 → cho phép mượn.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, maxBooks: 5, existingLoanCount: 0);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[0].Id, reader.Id, today);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Loan);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(1, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task Reader_WithThreeOfFiveLoans_CanBorrowWithinRemainingLimit()
    {
        // Bạn đọc đang mượn 3/5 → cho phép mượn thêm trong phạm vi còn lại.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, maxBooks: 5, existingLoanCount: 3);
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Mượn thêm sách thứ 4 (3 -> 4)
        var result1 = await service.CreateAsync(books[3].Id, reader.Id, today);
        Assert.True(result1.IsSuccess);
        Assert.Equal(4, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));

        // Mượn thêm sách thứ 5 (4 -> 5)
        var result2 = await service.CreateAsync(books[4].Id, reader.Id, today);
        Assert.True(result2.IsSuccess);
        Assert.Equal(5, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task Reader_WithFourOfFiveLoans_CanBorrowOneMoreBook()
    {
        // Bạn đọc đang mượn 4/5 → cho phép mượn thêm 1 sách.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, maxBooks: 5, existingLoanCount: 4);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[4].Id, reader.Id, today);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Loan);
        Assert.Equal(5, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task Reader_WithFiveOfFiveLoans_IsBlockedFromBorrowing()
    {
        // Bạn đọc đang mượn 5/5 → chặn cho mượn.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, maxBooks: 5, existingLoanCount: 5);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[5].Id, reader.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Loan);
        Assert.Equal("Bạn đang mượn 5/5 sách, không thể mượn thêm.", result.ErrorMessage);
        // Đảm bảo khi bị chặn thì phiếu mượn không được tạo
        Assert.Equal(5, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task Reader_WithFiveOfFiveLoans_ErrorMessageExplicitlyShowsFiveOfFive()
    {
        // Bạn đọc đang mượn 5/5 → thông báo phải thể hiện rõ 5/5.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, maxBooks: 5, existingLoanCount: 5);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[5].Id, reader.Id, today);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("5/5", result.ErrorMessage);
        Assert.Equal("Bạn đang mượn 5/5 sách, không thể mượn thêm.", result.ErrorMessage);
    }

    [Fact]
    public async Task Reader_WithFourOfFiveLoans_RequestingTwoBooks_IsBlockedDueToExceedingLimit()
    {
        // Bạn đọc đang mượn 4/5 nhưng yêu cầu mượn thêm 2 sách → chặn vì tổng số sẽ vượt hạn mức.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, maxBooks: 5, existingLoanCount: 4);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var requestedBookIds = new List<int> { books[4].Id, books[5].Id };
        var result = await service.CreateManyAsync(requestedBookIds, reader.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Loans);
        Assert.Contains("4/5", result.ErrorMessage);
        Assert.Contains("vượt quá hạn mức", result.ErrorMessage);
        Assert.Contains("5", result.ErrorMessage);
        // Đảm bảo khi bị chặn thì không có phiếu mượn nào được tạo
        Assert.Equal(4, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task BlockedLoan_EnsuresNoNewLoanRecordIsCreated()
    {
        // Đảm bảo khi bị chặn thì phiếu mượn không được tạo.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, maxBooks: 5, existingLoanCount: 5);
        var initialLoanCount = await db.BookLoans.CountAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[5].Id, reader.Id, today);

        Assert.False(result.IsSuccess);
        var finalLoanCount = await db.BookLoans.CountAsync();
        Assert.Equal(initialLoanCount, finalLoanCount);
    }

    [Fact]
    public async Task LoanController_Create_AtLimit_SetsErrorMessageAndBlocks()
    {
        // Kiểm tra qua Controller Create action khi bạn đọc đạt hạn mức 5/5
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, maxBooks: 5, existingLoanCount: 5);
        var controller = new LoanController(service, db);
        var httpContext = new DefaultHttpContext();
        var tempData = new TempDataDictionary(httpContext, (ITempDataProvider)new TestTempDataProvider());
        controller.TempData = tempData;

        var result = await controller.Create(new CreateBookLoanViewModel
        {
            BookId = books[5].Id,
            ReaderAccountId = reader.Id,
            LoanDate = DateOnly.FromDateTime(DateTime.Today)
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Bạn đang mượn 5/5 sách, không thể mượn thêm.", controller.TempData["ErrorMessage"]);
        Assert.Equal(5, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task LoanController_CreateApi_AtLimit_ReturnsBadRequestWithMessage()
    {
        // Kiểm tra qua API endpoint POST api/loans khi bạn đọc đạt hạn mức 5/5
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, maxBooks: 5, existingLoanCount: 5);
        var controller = new LoanController(service, db);

        var result = await controller.CreateApi(new CreateBookLoanViewModel
        {
            BookId = books[5].Id,
            ReaderAccountId = reader.Id,
            LoanDate = DateOnly.FromDateTime(DateTime.Today)
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var messageProp = badRequest.Value?.GetType().GetProperty("message")?.GetValue(badRequest.Value)?.ToString();
        Assert.NotNull(messageProp);
        Assert.Equal("Bạn đang mượn 5/5 sách, không thể mượn thêm.", messageProp);
        Assert.Equal(5, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task LoanController_CreateBatchApi_ExceedingLimit_ReturnsBadRequestAndBlocks()
    {
        // Kiểm tra qua API endpoint POST api/loans/batch khi mượn vượt hạn mức (đang 4/5, mượn 2)
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, maxBooks: 5, existingLoanCount: 4);
        var controller = new LoanController(service, db);

        var result = await controller.CreateBatchApi(new CreateBatchBookLoanViewModel
        {
            BookIds = [books[4].Id, books[5].Id],
            ReaderAccountId = reader.Id,
            LoanDate = DateOnly.FromDateTime(DateTime.Today)
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var messageProp = badRequest.Value?.GetType().GetProperty("message")?.GetValue(badRequest.Value)?.ToString();
        Assert.NotNull(messageProp);
        Assert.Contains("4/5", messageProp);
        Assert.Contains("vượt quá hạn mức", messageProp);
        Assert.Equal(4, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        private readonly Dictionary<string, object> data = new();
        public IDictionary<string, object> LoadTempData(HttpContext context) => data;
        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
            data.Clear();
            foreach (var kv in values) data[kv.Key] = kv.Value;
        }
    }
}

