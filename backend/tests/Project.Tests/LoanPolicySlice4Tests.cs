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

public class LoanPolicySlice4Tests
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
        int existingLoans = 0,
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
            FullName = "Nguyễn Văn Nhật Ký",
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
                Title = $"Sách Slice 4 #{i + 1}",
                Author = new Author { Name = $"Tác giả #{i + 1}" }
            };
            db.Books.Add(book);
            books.Add(book);
        }

        await db.SaveChangesAsync();

        for (var i = 0; i < existingLoans; i++)
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

        return (reader, books);
    }

    [Fact]
    public async Task BlockedByLimit_CreatesAuditLogWithLimitReason()
    {
        // Bị chặn do đạt hạn mức → có nhật ký ghi rõ lý do hạn mức
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, maxBooks: 3, existingLoans: 3);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[3].Id, reader.Id, today, actor: "thuthu@library.test");

        Assert.False(result.IsSuccess);
        var logs = await service.GetBlockedLoanLogsAsync(reader.Id);
        var log = Assert.Single(logs);
        Assert.Equal(reader.Id, log.ReaderAccountId);
        Assert.Equal(reader.FullName, log.ReaderName);
        Assert.Equal("thuthu@library.test", log.Operator);
        Assert.Contains("3/3", log.Reason);
        Assert.Contains("không thể mượn thêm", log.Reason);
        Assert.True(log.OccurredAtUtc <= DateTime.UtcNow);
        Assert.True(log.OccurredAtUtc >= DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task BlockedByExpiredCard_CreatesAuditLogWithExpiredReason()
    {
        // Bị chặn do thẻ hết hạn → có nhật ký ghi rõ lý do thẻ hết hạn
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, isCardExpired: true);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[0].Id, reader.Id, today, actor: "thuthu@library.test");

        Assert.False(result.IsSuccess);
        var logs = await service.GetBlockedLoanLogsAsync(reader.Id);
        var log = Assert.Single(logs);
        Assert.Equal(reader.Id, log.ReaderAccountId);
        Assert.Equal(reader.FullName, log.ReaderName);
        Assert.Equal("thuthu@library.test", log.Operator);
        Assert.Contains("hết hạn", log.Reason);
    }

    [Fact]
    public async Task BlockedByLockedCard_CreatesAuditLogWithLockedReason()
    {
        // Bị chặn do thẻ bị khoá → có nhật ký ghi rõ lý do thẻ đang bị khoá
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, isCardLocked: true);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[0].Id, reader.Id, today, actor: "thuthu@library.test");

        Assert.False(result.IsSuccess);
        var logs = await service.GetBlockedLoanLogsAsync(reader.Id);
        var log = Assert.Single(logs);
        Assert.Equal(reader.Id, log.ReaderAccountId);
        Assert.Equal(reader.FullName, log.ReaderName);
        Assert.Equal("thuthu@library.test", log.Operator);
        Assert.Contains("khoá", log.Reason);
    }

    [Fact]
    public async Task BlockedByOverdueLoan_CreatesAuditLogWithOverdueReason()
    {
        // Bị chặn do có phiếu mượn quá hạn chưa trả → có nhật ký ghi rõ lý do quá hạn
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db);
        var today = DateOnly.FromDateTime(DateTime.Today);

        db.BookLoans.Add(new BookLoan
        {
            BookId = books[0].Id,
            ReaderAccountId = reader.Id,
            LoanDate = today.AddDays(-20),
            OriginalDueDate = today.AddDays(-2),
            DueDate = today.AddDays(-2)
        });
        await db.SaveChangesAsync();

        var result = await service.CreateAsync(books[1].Id, reader.Id, today, actor: "thuthu@library.test");

        Assert.False(result.IsSuccess);
        var logs = await service.GetBlockedLoanLogsAsync(reader.Id);
        var log = Assert.Single(logs);
        Assert.Equal(reader.Id, log.ReaderAccountId);
        Assert.Equal(reader.FullName, log.ReaderName);
        Assert.Equal("thuthu@library.test", log.Operator);
        Assert.Contains("quá hạn", log.Reason);
    }

    [Fact]
    public async Task BlockedByUnpaidFee_CreatesAuditLogWithDebtReason()
    {
        // Bị chặn do còn nợ phí → có nhật ký ghi rõ lý do còn nợ phí và số tiền nợ
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, outstandingBalance: 50000m);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[0].Id, reader.Id, today, actor: "thuthu@library.test");

        Assert.False(result.IsSuccess);
        var logs = await service.GetBlockedLoanLogsAsync(reader.Id);
        var log = Assert.Single(logs);
        Assert.Equal(reader.Id, log.ReaderAccountId);
        Assert.Equal(reader.FullName, log.ReaderName);
        Assert.Equal("thuthu@library.test", log.Operator);
        Assert.Contains("50.000 VND", log.Reason);
    }

    [Fact]
    public async Task SuccessfulLoan_DoesNotCreateBlockedLoanLog()
    {
        // Kiểm tra trường hợp cho mượn thành công → không tạo nhật ký bị chặn
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[0].Id, reader.Id, today, actor: "thuthu@library.test");

        Assert.True(result.IsSuccess);
        var logs = await service.GetBlockedLoanLogsAsync();
        Assert.Empty(logs);
        Assert.Empty(await db.AuditLogs.Where(l => l.Action == AuditActions.BlockLoan).ToListAsync());
    }

    [Fact]
    public async Task MultipleBlocksOfSameReader_RecordedAccuratelyAndDistinguishable()
    {
        // Kiểm tra nhiều lần bị chặn của cùng một bạn đọc → mỗi lần được ghi nhận đúng
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, maxBooks: 1, existingLoans: 1);
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Lần 1: Chặn do đạt hạn mức
        var result1 = await service.CreateAsync(books[1].Id, reader.Id, today, actor: "staff1@library.test");
        Assert.False(result1.IsSuccess);

        // Giả lập bạn đọc phát sinh thêm nợ phí
        reader.OutstandingBalance = 75000m;
        await db.SaveChangesAsync();

        // Lần 2: Chặn do vừa đạt hạn mức vừa nợ phí
        var result2 = await service.CreateAsync(books[2].Id, reader.Id, today, actor: "staff2@library.test");
        Assert.False(result2.IsSuccess);

        var logs = await service.GetBlockedLoanLogsAsync(reader.Id);
        Assert.Equal(2, logs.Count);

        // Kiểm tra 2 bản ghi phân biệt rõ ràng
        var newest = logs[0];
        var oldest = logs[1];

        Assert.Equal("staff2@library.test", newest.Operator);
        Assert.Contains("75.000 VND", newest.Reason);
        Assert.Contains("1/1", newest.Reason);

        Assert.Equal("staff1@library.test", oldest.Operator);
        Assert.Contains("1/1", oldest.Reason);
        Assert.DoesNotContain("75.000 VND", oldest.Reason);
    }

    [Fact]
    public async Task BlockWithMultipleViolations_RetainsAllReasonsInLog()
    {
        // Kiểm tra trường hợp một lần cho mượn vi phạm nhiều điều kiện → không làm mất lý do vi phạm
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, maxBooks: 2, existingLoans: 2, outstandingBalance: 60000m, isCardExpired: true);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateAsync(books[2].Id, reader.Id, today, actor: "manager@library.test");

        Assert.False(result.IsSuccess);
        var logs = await service.GetBlockedLoanLogsAsync(reader.Id);
        var log = Assert.Single(logs);

        Assert.Equal("manager@library.test", log.Operator);
        Assert.Contains("hết hạn", log.Reason);
        Assert.Contains("60.000 VND", log.Reason);
        Assert.Contains("2/2", log.Reason);
    }

    [Fact]
    public async Task ExistingLogEntry_IsNotModifiedWhenNewLogOccurs()
    {
        // Kiểm tra bản ghi cũ không bị thay đổi khi phát sinh bản ghi mới (tính bất biến append-only)
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, maxBooks: 1, existingLoans: 1);
        var today = DateOnly.FromDateTime(DateTime.Today);

        await service.CreateAsync(books[1].Id, reader.Id, today, actor: "first-staff@library.test");
        var log1 = (await service.GetBlockedLoanLogsAsync(reader.Id)).Single();
        var log1Id = log1.Id;
        var log1Occurred = log1.OccurredAtUtc;
        var log1Reason = log1.Reason;
        var log1Operator = log1.Operator;

        // Phát sinh bản ghi mới
        await service.CreateAsync(books[2].Id, reader.Id, today, actor: "second-staff@library.test");

        var allLogs = await service.GetBlockedLoanLogsAsync(reader.Id);
        Assert.Equal(2, allLogs.Count);

        var originalLog = allLogs.First(l => l.Id == log1Id);
        Assert.Equal(log1Occurred, originalLog.OccurredAtUtc);
        Assert.Equal(log1Reason, originalLog.Reason);
        Assert.Equal(log1Operator, originalLog.Operator);
    }

    [Fact]
    public async Task Controller_Create_WhenBlocked_PersistsBlockedLogAndIsRetrievableInViewModel()
    {
        // Kiểm tra luồng MVC Controller Create khi bị chặn: lưu nhật ký và hiển thị trong ViewModel
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

        // Kiểm tra nhật ký đã được lưu
        var logs = await service.GetBlockedLoanLogsAsync(reader.Id);
        var log = Assert.Single(logs);
        Assert.Equal(reader.FullName, log.ReaderName);
        Assert.Contains("hết hạn", log.Reason);

        // Kiểm tra danh sách hiển thị trong LoanIndexViewModel
        var allLogs = await service.GetBlockedLoanLogsAsync();
        var model = new LoanIndexViewModel { BlockedLoanLogs = allLogs };
        Assert.NotEmpty(model.BlockedLoanLogs);
        Assert.Contains(model.BlockedLoanLogs, l => l.ReaderAccountId == reader.Id && l.Reason.Contains("hết hạn"));
    }

    [Fact]
    public async Task Controller_CreateApi_WhenBlocked_PersistsBlockedLog()
    {
        // Kiểm tra API POST api/loans khi bị chặn: ghi nhận nhật ký
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, outstandingBalance: 120000m);
        var controller = new LoanController(service, db);

        var result = await controller.CreateApi(new CreateBookLoanViewModel
        {
            BookId = books[0].Id,
            ReaderAccountId = reader.Id,
            LoanDate = DateOnly.FromDateTime(DateTime.Today)
        });

        Assert.IsType<BadRequestObjectResult>(result);
        var logs = await service.GetBlockedLoanLogsAsync(reader.Id);
        var log = Assert.Single(logs);
        Assert.Contains("120.000 VND", log.Reason);
    }

    [Fact]
    public async Task Controller_CreateBatchApi_WhenBlocked_PersistsBlockedLog()
    {
        // Kiểm tra API POST api/loans/batch khi bị chặn: ghi nhận nhật ký
        var (db, service) = CreateTestEnvironment();
        var (reader, books) = await SeedReaderAndBooksAsync(db, maxBooks: 2, existingLoans: 1);
        var controller = new LoanController(service, db);

        // Đang mượn 1/2, yêu cầu mượn thêm 2 -> vượt quá hạn mức
        var result = await controller.CreateBatchApi(new CreateBatchBookLoanViewModel
        {
            BookIds = [books[1].Id, books[2].Id],
            ReaderAccountId = reader.Id,
            LoanDate = DateOnly.FromDateTime(DateTime.Today)
        });

        Assert.IsType<BadRequestObjectResult>(result);
        var logs = await service.GetBlockedLoanLogsAsync(reader.Id);
        var log = Assert.Single(logs);
        Assert.Contains("1/2", log.Reason);
        Assert.Contains("vượt quá hạn mức", log.Reason);
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

