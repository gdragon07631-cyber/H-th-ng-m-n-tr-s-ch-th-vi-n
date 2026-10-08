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

public class LoanPolicySlice2Tests
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
        ApplicationDbContext db,
        int maxBooks = 5,
        bool isCardExpired = false,
        bool isCardLocked = false,
        bool isReaderLocked = false)
    {
        var cardType = new LibraryCardType
        {
            Name = "Thẻ sinh viên",
            MaxBooks = maxBooks
        };
        db.LibraryCardTypes.Add(cardType);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var reader = new ReaderAccount
        {
            FullName = "Nguyễn Văn Bạn Đọc",
            DateOfBirth = new DateOnly(2000, 1, 1),
            Email = $"reader-{Guid.NewGuid():N}@example.test",
            PhoneNumber = "0912345678",
            StudentOrStaffCode = $"SV-{Guid.NewGuid():N}"[..8],
            PasswordHash = "hash",
            Status = "Đang hoạt động",
            IsLocked = isReaderLocked,
            LibraryCard = new LibraryCard
            {
                CardCode = $"CARD-{Guid.NewGuid():N}"[..10],
                LibraryCardType = cardType,
                IssuedOn = today.AddYears(-1),
                ExpiresOn = isCardExpired ? today.AddDays(-1) : today.AddYears(1),
                Status = isCardLocked ? "Bị khoá" : "Đang hoạt động"
            }
        };
        db.ReaderAccounts.Add(reader);

        var books = new List<Book>();
        for (var i = 0; i < 10; i++)
        {
            var book = new Book
            {
                Title = $"Sách Slice 2 #{i + 1}",
                Author = new Author { Name = $"Tác giả #{i + 1}" }
            };
            db.Books.Add(book);
            books.Add(book);
        }

        await db.SaveChangesAsync();
        return (reader, books);
    }

    [Fact]
    public async Task ValidCard_NotLocked_NoOverdue_CanBorrowWithinSlice1Limit()
    {
        // Thẻ còn hạn, không bị khoá, không có phiếu quá hạn → cho phép tiếp tục cho mượn nếu không vi phạm hạn mức Lát 1.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[0].Id, reader.Id, today);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Loan);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(1, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task ExpiredCard_BlocksLoan_AndDisplaysClearErrorMessage()
    {
        // Thẻ đã hết hạn → chặn cho mượn.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, isCardExpired: true);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[0].Id, reader.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Loan);
        Assert.Contains("hết hạn", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Thẻ bạn đọc đã hết hạn.", result.ErrorMessage);
        // Khi bị chặn, không được tạo phiếu mượn
        Assert.Equal(0, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task LockedCard_StatusBiKhoa_BlocksLoan_AndDisplaysClearErrorMessage()
    {
        // Thẻ bị khoá → chặn cho mượn.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, isCardLocked: true);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[0].Id, reader.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Loan);
        Assert.Contains("khoá", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Thẻ bạn đọc đang bị khoá.", result.ErrorMessage);
        // Khi bị chặn, không được tạo phiếu mượn
        Assert.Equal(0, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task LockedCard_ReaderIsLocked_BlocksLoan_AndDisplaysClearErrorMessage()
    {
        // Thẻ bị khoá do tài khoản bạn đọc bị khoá → chặn cho mượn.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, isReaderLocked: true);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[0].Id, reader.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Loan);
        Assert.Contains("khoá", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task LockedCard_CardIsLockedProperty_BlocksLoan()
    {
        // Thẻ bị khoá qua thuộc tính IsLocked trên LibraryCard → chặn cho mượn.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db);
        reader.LibraryCard!.IsLocked = true;
        await db.SaveChangesAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[0].Id, reader.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Contains("khoá", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task OverdueUnreturnedLoan_BlocksLoan_AndDisplaysClearErrorMessage()
    {
        // Có phiếu mượn quá hạn chưa trả → chặn cho mượn.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db);
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Tạo 1 phiếu mượn đã quá hạn (hạn trả là hôm qua) và chưa trả
        db.BookLoans.Add(new BookLoan
        {
            BookId = books[0].Id,
            ReaderAccountId = reader.Id,
            LoanDate = today.AddDays(-15),
            OriginalDueDate = today.AddDays(-1),
            DueDate = today.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var result = await service.CreateAsync(books[1].Id, reader.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Loan);
        Assert.Contains("quá hạn", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Bạn đọc đang có phiếu mượn quá hạn chưa trả.", result.ErrorMessage);
        // Không tạo thêm phiếu mượn
        Assert.Equal(1, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task ExistingLoanNotYetDue_DoesNotBlockForOverdue()
    {
        // Có phiếu mượn nhưng chưa đến hạn → không chặn vì lý do quá hạn.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db);
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Phiếu mượn còn 5 ngày nữa mới đến hạn
        db.BookLoans.Add(new BookLoan
        {
            BookId = books[0].Id,
            ReaderAccountId = reader.Id,
            LoanDate = today.AddDays(-2),
            OriginalDueDate = today.AddDays(5),
            DueDate = today.AddDays(5)
        });
        await db.SaveChangesAsync();

        var result = await service.CreateAsync(books[1].Id, reader.Id, today);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Loan);
        Assert.Equal(2, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task ReturnedLoan_EvenIfPastDueDate_DoesNotBlockForOverdue()
    {
        // Có phiếu mượn đã trả → không chặn vì lý do quá hạn.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db);
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Phiếu mượn có hạn trả là hôm qua nhưng đã trả
        db.BookLoans.Add(new BookLoan
        {
            BookId = books[0].Id,
            ReaderAccountId = reader.Id,
            LoanDate = today.AddDays(-15),
            OriginalDueDate = today.AddDays(-1),
            DueDate = today.AddDays(-1),
            IsReturned = true,
            ReturnDate = today.AddDays(-1),
            Status = "Đã trả"
        });
        await db.SaveChangesAsync();

        var result = await service.CreateAsync(books[1].Id, reader.Id, today);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Loan);
        Assert.Equal(2, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task ExpiredAndLockedCard_BlocksAndShowsBothReasons()
    {
        // Thẻ hết hạn và bị khoá cùng lúc → vẫn phải chặn và hiển thị lý do phù hợp.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, isCardExpired: true, isCardLocked: true);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[0].Id, reader.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Contains("khoá", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hết hạn", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task ExpiredCard_AndOverdueLoan_BlocksAndShowsBothReasons()
    {
        // Thẻ hết hạn đồng thời có phiếu quá hạn → vẫn phải chặn.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, isCardExpired: true);
        var today = DateOnly.FromDateTime(DateTime.Today);

        db.BookLoans.Add(new BookLoan
        {
            BookId = books[0].Id,
            ReaderAccountId = reader.Id,
            LoanDate = today.AddDays(-20),
            OriginalDueDate = today.AddDays(-5),
            DueDate = today.AddDays(-5)
        });
        await db.SaveChangesAsync();

        var result = await service.CreateAsync(books[1].Id, reader.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Contains("hết hạn", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("quá hạn", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        // Không tạo thêm phiếu mượn
        Assert.Equal(1, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task LockedCard_AndOverdueLoan_BlocksAndShowsBothReasons()
    {
        // Thẻ bị khoá đồng thời có phiếu quá hạn → vẫn phải chặn.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, isCardLocked: true);
        var today = DateOnly.FromDateTime(DateTime.Today);

        db.BookLoans.Add(new BookLoan
        {
            BookId = books[0].Id,
            ReaderAccountId = reader.Id,
            LoanDate = today.AddDays(-20),
            OriginalDueDate = today.AddDays(-5),
            DueDate = today.AddDays(-5)
        });
        await db.SaveChangesAsync();

        var result = await service.CreateAsync(books[1].Id, reader.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Contains("khoá", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("quá hạn", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task ReachedLimitSlice1_AndViolatesSlice2_BlocksLoan()
    {
        // Đồng thời đạt hạn mức Lát 1 và vi phạm một điều kiện của Lát 2 → không được cho mượn.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, maxBooks: 3, isCardExpired: true);
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Đã mượn 3 sách (đạt hạn mức 3/3)
        for (var i = 0; i < 3; i++)
        {
            db.BookLoans.Add(new BookLoan
            {
                BookId = books[i].Id,
                ReaderAccountId = reader.Id,
                LoanDate = today.AddDays(-5),
                OriginalDueDate = today.AddDays(5),
                DueDate = today.AddDays(5)
            });
        }
        await db.SaveChangesAsync();

        var result = await service.CreateAsync(books[3].Id, reader.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Contains("hết hạn", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3/3", result.ErrorMessage);
        // Không tạo thêm phiếu mượn
        Assert.Equal(3, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task Controller_Create_ExpiredCard_BlocksAndSetsTempDataErrorMessage()
    {
        // Kiểm tra qua Controller Create action khi thẻ hết hạn
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, isCardExpired: true);
        var controller = new LoanController(service, db);
        var httpContext = new DefaultHttpContext();
        var tempData = new TempDataDictionary(httpContext, (ITempDataProvider)new TestTempDataProvider());
        controller.TempData = tempData;

        var result = await controller.Create(new CreateBookLoanViewModel
        {
            BookId = books[0].Id,
            ReaderAccountId = reader.Id,
            LoanDate = DateOnly.FromDateTime(DateTime.Today)
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Thẻ bạn đọc đã hết hạn.", controller.TempData["ErrorMessage"]);
        Assert.Equal(0, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task Controller_CreateApi_LockedCard_ReturnsBadRequestWithMessage()
    {
        // Kiểm tra qua API endpoint POST api/loans khi thẻ bị khoá
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, isCardLocked: true);
        var controller = new LoanController(service, db);

        var result = await controller.CreateApi(new CreateBookLoanViewModel
        {
            BookId = books[0].Id,
            ReaderAccountId = reader.Id,
            LoanDate = DateOnly.FromDateTime(DateTime.Today)
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var messageProp = badRequest.Value?.GetType().GetProperty("message")?.GetValue(badRequest.Value)?.ToString();
        Assert.NotNull(messageProp);
        Assert.Contains("khoá", messageProp, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task Controller_CreateBatchApi_OverdueLoan_ReturnsBadRequestWithMessage()
    {
        // Kiểm tra qua API endpoint POST api/loans/batch khi có phiếu quá hạn chưa trả
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db);
        var today = DateOnly.FromDateTime(DateTime.Today);

        db.BookLoans.Add(new BookLoan
        {
            BookId = books[0].Id,
            ReaderAccountId = reader.Id,
            LoanDate = today.AddDays(-15),
            OriginalDueDate = today.AddDays(-1),
            DueDate = today.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var controller = new LoanController(service, db);
        var result = await controller.CreateBatchApi(new CreateBatchBookLoanViewModel
        {
            BookIds = [books[1].Id, books[2].Id],
            ReaderAccountId = reader.Id,
            LoanDate = today
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var messageProp = badRequest.Value?.GetType().GetProperty("message")?.GetValue(badRequest.Value)?.ToString();
        Assert.NotNull(messageProp);
        Assert.Contains("quá hạn", messageProp, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
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
