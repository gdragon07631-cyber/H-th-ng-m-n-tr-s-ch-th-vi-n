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

public class LoanPolicySlice3Tests
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
        decimal outstandingBalance = 0m,
        bool isCardExpired = false,
        bool isCardLocked = false)
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
            OutstandingBalance = outstandingBalance,
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
                Title = $"Sách Slice 3 #{i + 1}",
                Author = new Author { Name = $"Tác giả #{i + 1}" }
            };
            db.Books.Add(book);
            books.Add(book);
        }

        await db.SaveChangesAsync();
        return (reader, books);
    }

    [Fact]
    public void FormatVnd_FormatsVariousAmountsCorrectly()
    {
        // Kiểm tra định dạng số tiền theo VND: 50.000 VND, 100.000 VND, 1.250.000 VND
        Assert.Equal("50.000 VND", BookLoanService.FormatVnd(50000m));
        Assert.Equal("100.000 VND", BookLoanService.FormatVnd(100000m));
        Assert.Equal("1.250.000 VND", BookLoanService.FormatVnd(1250000m));
        Assert.Equal("40.000 VND", BookLoanService.FormatVnd(40000m));
        Assert.Equal("0 VND", BookLoanService.FormatVnd(0m));
    }

    [Fact]
    public async Task Reader_WithNoUnpaidFee_CanBorrow()
    {
        // Bạn đọc không có khoản phí chưa thanh toán → cho phép tiếp tục nếu không vi phạm điều kiện khác.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, outstandingBalance: 0m);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[0].Id, reader.Id, today);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Loan);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(1, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task Reader_WithSingleUnpaidFee_IsBlocked_AndDisplaysAccurateDebtMessage()
    {
        // Bạn đọc có một khoản nợ chưa thanh toán → chặn.
        // Hiển thị: Bạn còn nợ 50.000 VND, không thể mượn sách.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db);
        reader.Fees.Add(new ReaderFee
        {
            Amount = 50000m,
            PaidAmount = 0m,
            IsPaid = false,
            Status = "Chưa thanh toán"
        });
        await db.SaveChangesAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[0].Id, reader.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Loan);
        Assert.Equal("Bạn còn nợ 50.000 VND, không thể mượn sách.", result.ErrorMessage);
        // Khi bị chặn vì nợ phí, phiếu mượn không được tạo
        Assert.Equal(0, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task Reader_WithOutstandingBalanceColumn_IsBlocked_AndDisplaysAccurateDebtMessage()
    {
        // Bạn đọc có nợ qua trường OutstandingBalance trực tiếp trên ReaderAccount → chặn.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, outstandingBalance: 50000m);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[0].Id, reader.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Loan);
        Assert.Equal("Bạn còn nợ 50.000 VND, không thể mượn sách.", result.ErrorMessage);
        Assert.Equal(0, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task Reader_WithMultipleUnpaidFees_SumsTotalAccurately_AndBlocks()
    {
        // Bạn đọc có nhiều khoản nợ chưa thanh toán → cộng chính xác tổng số tiền và chặn.
        // Ví dụ: Khoản 1: 20.000 VND, Khoản 2: 30.000 VND → Tổng 50.000 VND.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db);
        reader.Fees.Add(new ReaderFee { Amount = 20000m, PaidAmount = 0m, IsPaid = false });
        reader.Fees.Add(new ReaderFee { Amount = 30000m, PaidAmount = 0m, IsPaid = false });
        await db.SaveChangesAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[0].Id, reader.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Loan);
        Assert.Equal("Bạn còn nợ 50.000 VND, không thể mượn sách.", result.ErrorMessage);
        Assert.Equal(0, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task Reader_WithFullyPaidFees_DoesNotBlockForDebt()
    {
        // Bạn đọc đã thanh toán toàn bộ các khoản phí → không chặn vì nợ phí.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db);
        reader.Fees.Add(new ReaderFee
        {
            Amount = 100000m,
            PaidAmount = 100000m,
            IsPaid = true,
            Status = "Đã thanh toán"
        });
        await db.SaveChangesAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[0].Id, reader.Id, today);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Loan);
        Assert.Equal(1, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task Reader_WithPartiallyPaidFee_CalculatesRemainingAmountOnly()
    {
        // Bạn đọc đã thanh toán một phần một khoản phí → chỉ tính phần còn thiếu.
        // Ví dụ: Khoản phí 100.000 VND, đã thanh toán 60.000 VND → Còn nợ 40.000 VND.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db);
        reader.Fees.Add(new ReaderFee
        {
            Amount = 100000m,
            PaidAmount = 60000m,
            IsPaid = false,
            Status = "Thanh toán một phần"
        });
        await db.SaveChangesAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[0].Id, reader.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Loan);
        Assert.Equal("Bạn còn nợ 40.000 VND, không thể mượn sách.", result.ErrorMessage);
        Assert.Equal(0, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task Reader_WithZeroTotalDebt_DoesNotBlock()
    {
        // Tổng nợ bằng 0 → không chặn vì nợ phí.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, outstandingBalance: 0m);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[0].Id, reader.Id, today);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Loan);
        Assert.Equal(1, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task Reader_WithPositiveTotalDebt_IsBlocked()
    {
        // Tổng nợ là số tiền dương → chặn.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, outstandingBalance: 1250000m);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[0].Id, reader.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Loan);
        Assert.Equal("Bạn còn nợ 1.250.000 VND, không thể mượn sách.", result.ErrorMessage);
        Assert.Equal(0, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task Reader_WithDebt_AndAtLimit_BlocksAndShowsBothReasons()
    {
        // Có nợ phí đồng thời đạt hạn mức → vẫn chặn và không tạo phiếu mượn.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, maxBooks: 3, outstandingBalance: 50000m);
        var today = DateOnly.FromDateTime(DateTime.Today);

        for (var i = 0; i < 3; i++)
        {
            db.BookLoans.Add(new BookLoan
            {
                BookId = books[i].Id,
                ReaderAccountId = reader.Id,
                LoanDate = today.AddDays(-2),
                OriginalDueDate = today.AddDays(10),
                DueDate = today.AddDays(10)
            });
        }
        await db.SaveChangesAsync();

        var result = await service.CreateAsync(books[3].Id, reader.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Contains("50.000 VND", result.ErrorMessage);
        Assert.Contains("3/3", result.ErrorMessage);
        Assert.Equal(3, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task Reader_WithDebt_AndExpiredCard_BlocksAndShowsBothReasons()
    {
        // Có nợ phí đồng thời thẻ hết hạn → vẫn chặn và không tạo phiếu mượn.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, outstandingBalance: 50000m, isCardExpired: true);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[0].Id, reader.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Contains("50.000 VND", result.ErrorMessage);
        Assert.Contains("hết hạn", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task Reader_WithDebt_AndLockedCard_BlocksAndShowsBothReasons()
    {
        // Có nợ phí đồng thời thẻ bị khoá → vẫn chặn và không tạo phiếu mượn.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, outstandingBalance: 50000m, isCardLocked: true);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[0].Id, reader.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Contains("50.000 VND", result.ErrorMessage);
        Assert.Contains("khoá", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task Reader_WithDebt_AndOverdueLoan_BlocksAndShowsBothReasons()
    {
        // Có nợ phí đồng thời có phiếu quá hạn → vẫn phải chặn và không tạo phiếu mượn.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, outstandingBalance: 50000m);
        var today = DateOnly.FromDateTime(DateTime.Today);

        db.BookLoans.Add(new BookLoan
        {
            BookId = books[0].Id,
            ReaderAccountId = reader.Id,
            LoanDate = today.AddDays(-20),
            OriginalDueDate = today.AddDays(-3),
            DueDate = today.AddDays(-3)
        });
        await db.SaveChangesAsync();

        var result = await service.CreateAsync(books[1].Id, reader.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Contains("50.000 VND", result.ErrorMessage);
        Assert.Contains("quá hạn", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task Reader_NoDebt_AndNoSlice1Slice2Violation_AllowsBorrowing()
    {
        // Không có nợ phí và không vi phạm các điều kiện Lát 1, Lát 2 → cho phép cho mượn bình thường.
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, outstandingBalance: 0m);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[0].Id, reader.Id, today);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Loan);
        Assert.Equal(1, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task Controller_Create_WithDebt_SetsErrorMessageAndBlocks()
    {
        // Kiểm tra qua Controller Create action khi bạn đọc còn nợ phí
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, outstandingBalance: 50000m);
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
        Assert.Equal("Bạn còn nợ 50.000 VND, không thể mượn sách.", controller.TempData["ErrorMessage"]);
        Assert.Equal(0, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task Controller_CreateApi_WithDebt_ReturnsBadRequestWithMessage()
    {
        // Kiểm tra qua API POST api/loans khi bạn đọc còn nợ phí
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, outstandingBalance: 50000m);
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
        Assert.Equal("Bạn còn nợ 50.000 VND, không thể mượn sách.", messageProp);
        Assert.Equal(0, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
    }

    [Fact]
    public async Task Controller_CreateBatchApi_WithDebt_ReturnsBadRequestWithMessage()
    {
        // Kiểm tra qua API POST api/loans/batch khi bạn đọc còn nợ phí
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, outstandingBalance: 100000m);
        var controller = new LoanController(service, db);

        var result = await controller.CreateBatchApi(new CreateBatchBookLoanViewModel
        {
            BookIds = [books[0].Id, books[1].Id],
            ReaderAccountId = reader.Id,
            LoanDate = DateOnly.FromDateTime(DateTime.Today)
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var messageProp = badRequest.Value?.GetType().GetProperty("message")?.GetValue(badRequest.Value)?.ToString();
        Assert.NotNull(messageProp);
        Assert.Equal("Bạn còn nợ 100.000 VND, không thể mượn sách.", messageProp);
        Assert.Equal(0, await db.BookLoans.CountAsync(l => l.ReaderAccountId == reader.Id));
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

