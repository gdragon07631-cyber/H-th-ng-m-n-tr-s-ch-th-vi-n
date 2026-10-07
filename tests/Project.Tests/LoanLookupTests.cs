using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Project.Controllers;
using Project.Data;
using Project.Filters;
using Project.Models;
using Project.Services;

namespace Project.Tests;

public sealed class LoanLookupTests : IDisposable
{
    private static readonly DateOnly BaseDate = new(2026, 9, 1);
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly ApplicationDbContext db;
    private readonly LoanLookupService service;
    private Book? book;
    private int readerSequence;

    public LoanLookupTests()
    {
        connection.Open();
        db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        service = new LoanLookupService(db);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    [Fact]
    public async Task FindsLoansByLibraryCardCode()
    {
        var reader = await AddReaderAsync("CARD-001", "Nguyễn Văn A");
        var other = await AddReaderAsync("CARD-002");
        var loan = await AddLoanAsync(reader, BaseDate);
        await AddLoanAsync(other, BaseDate);

        var result = await service.SearchAsync("CARD-001", 1);

        var item = Assert.Single(result!.Items);
        Assert.Equal(loan.Id, item.LoanId);
        Assert.Equal("CARD-001", item.CardCode);
        Assert.Equal("Nguyễn Văn A", item.ReaderName);
        Assert.Equal(loan.LoanDate, item.LoanDate);
        Assert.Equal(loan.DueDate, item.DueDate);
        Assert.Equal(LoanLookupStatus.Open, item.Status);
        Assert.Equal(1, result.TotalItems);
    }

    [Theory]
    [InlineData("{0}")]
    [InlineData("#{0}")]
    [InlineData("  {0}  ")]
    public async Task FindsLoanByLoanCode(string format)
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoanAsync(reader, BaseDate);
        var target = await AddLoanAsync(reader, BaseDate.AddDays(1));

        var result = await service.SearchAsync(string.Format(format, target.Id), 1);

        Assert.Equal(target.Id, Assert.Single(result!.Items).LoanId);
    }

    [Fact]
    public async Task CodeIsTrimmedButMatchedExactly()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoanAsync(reader, BaseDate);

        Assert.Single((await service.SearchAsync("  CARD-001\t", 1))!.Items);
        Assert.Equal(0, (await service.SearchAsync("CARD-00", 1))!.TotalItems);
        Assert.Equal(0, (await service.SearchAsync("CARD-0011", 1))!.TotalItems);
    }

    [Fact]
    public async Task CopyBarcodeDoesNotGuessLoansFromTheCopysBook()
    {
        // BookLoan chưa lưu bản sao được mượn: mã vạch không được suy ra thành mọi phiếu cùng đầu sách.
        var reader = await AddReaderAsync("CARD-001");
        var loan = await AddLoanAsync(reader, BaseDate);
        await AddCopyAsync(loan.BookId, "BC-0001");

        var result = await service.SearchAsync("BC-0001", 1);

        Assert.NotNull(result);
        Assert.Equal(0, result!.TotalItems);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task CodeMatchingBothCardAndCopyBarcodeReturnsTheCardsLoansOnce()
    {
        var reader = await AddReaderAsync("SHARED-01");
        var first = await AddLoanAsync(reader, BaseDate);
        var second = await AddLoanAsync(reader, BaseDate.AddDays(1));
        await AddCopyAsync(first.BookId, "SHARED-01");

        var result = await service.SearchAsync("SHARED-01", 1);

        Assert.Equal([second.Id, first.Id], result!.Items.Select(item => item.LoanId));
        Assert.Equal(2, result.TotalItems);
    }

    [Fact]
    public async Task CodeMatchingLoanIdAndCardCodeMergesWithoutDuplicates()
    {
        var owner = await AddReaderAsync("TEMP-A");
        var someoneElse = await AddReaderAsync("TEMP-B");
        var ownLoan = await AddLoanAsync(owner, BaseDate);
        var otherLoan = await AddLoanAsync(someoneElse, BaseDate.AddDays(2));
        var ownSecondLoan = await AddLoanAsync(owner, BaseDate.AddDays(1));

        // Thẻ của owner mang mã trùng mã phiếu của chính owner (ownLoan) → không được lặp phiếu đó.
        await SetCardCodeAsync(owner, ownLoan.Id.ToString());
        var sameOwner = await service.SearchAsync(ownLoan.Id.ToString(), 1);
        Assert.Equal([ownSecondLoan.Id, ownLoan.Id], sameOwner!.Items.Select(item => item.LoanId));
        Assert.Equal(2, sameOwner.TotalItems);

        // Thẻ của owner mang mã trùng mã phiếu của người khác → trả đủ cả phiếu đó và phiếu của owner.
        await SetCardCodeAsync(owner, otherLoan.Id.ToString());
        var merged = await service.SearchAsync(otherLoan.Id.ToString(), 1);
        Assert.Equal([otherLoan.Id, ownSecondLoan.Id, ownLoan.Id], merged!.Items.Select(item => item.LoanId));
        Assert.Equal(3, merged.TotalItems);
    }

    [Fact]
    public async Task ResultsAreOrderedByLoanDateThenIdDescending()
    {
        var reader = await AddReaderAsync("CARD-001");
        var older = await AddLoanAsync(reader, BaseDate);
        var sameDayFirst = await AddLoanAsync(reader, BaseDate.AddDays(5));
        var newest = await AddLoanAsync(reader, BaseDate.AddDays(9));
        var sameDaySecond = await AddLoanAsync(reader, BaseDate.AddDays(5));

        var result = await service.SearchAsync("CARD-001", 1);

        Assert.Equal([newest.Id, sameDaySecond.Id, sameDayFirst.Id, older.Id], result!.Items.Select(item => item.LoanId));
        Assert.All(result.Items, item => Assert.Equal(LoanLookupStatus.Open, item.Status));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 1)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    public async Task FirstPageHoldsAtMostTwentyLoans(int loanCount, int expectedOnFirstPage, int expectedPages)
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoansAsync(reader, loanCount);

        var result = await service.SearchAsync("CARD-001", 1);

        Assert.Equal(expectedOnFirstPage, result!.Items.Count);
        Assert.Equal(loanCount, result.TotalItems);
        Assert.Equal(expectedPages, result.TotalPages);
        Assert.Equal(1, result.Page);
        Assert.Equal(LoanLookupService.PageSize, result.PageSize);
    }

    [Fact]
    public async Task TwentyOneLoansSplitIntoTwentyAndOneWithoutOverlap()
    {
        var reader = await AddReaderAsync("CARD-001");
        var loans = await AddLoansAsync(reader, 21);

        var first = await service.SearchAsync("CARD-001", 1);
        var second = await service.SearchAsync("CARD-001", 2);

        Assert.Equal(20, first!.Items.Count);
        var last = Assert.Single(second!.Items);
        Assert.Equal(2, second.Page);
        Assert.Equal(21, second.TotalItems);
        // Phiếu cũ nhất nằm cuối cùng, ở trang 2; hai trang không chồng lấn và đủ 21 phiếu.
        Assert.Equal(loans.MinBy(loan => loan.LoanDate)!.Id, last.LoanId);
        Assert.Equal(21, first.Items.Select(item => item.LoanId).Append(last.LoanId).Distinct().Count());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(99, 2)]
    [InlineData(int.MaxValue, 2)]
    public async Task OutOfRangePageIsClampedLikeTheCatalog(int requestedPage, int expectedPage)
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoansAsync(reader, 21);

        var result = await service.SearchAsync("CARD-001", requestedPage);

        Assert.Equal(expectedPage, result!.Page);
        Assert.NotEmpty(result.Items);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyCodeReturnsNoResultInsteadOfEveryLoan(string? code)
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoanAsync(reader, BaseDate);

        Assert.Null(await service.SearchAsync(code, 1));
    }

    [Theory]
    [InlineData("KHONG-TON-TAI")]
    [InlineData("999999")]
    [InlineData("#")]
    [InlineData("-1")]
    public async Task UnknownCodeReturnsEmptyPage(string code)
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoanAsync(reader, BaseDate);

        var result = await service.SearchAsync(code, 1);

        Assert.NotNull(result);
        Assert.Equal(0, result!.TotalItems);
        Assert.Equal(0, result.TotalPages);
        Assert.Equal(1, result.Page);
    }

    [Fact]
    public async Task PageShowsNotFoundMessageForUnknownCode()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoanAsync(reader, BaseDate);

        var model = await IndexModelAsync(CreateController(), "KHONG-TON-TAI");

        Assert.Equal(0, model.Result!.TotalItems);
        Assert.Null(model.ErrorMessage);
        Assert.Null(model.ValidationMessage);
    }

    [Fact]
    public async Task PageAsksForACodeWhenTheBoxIsEmpty()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoanAsync(reader, BaseDate);
        var controller = CreateController();

        var empty = await IndexModelAsync(controller, "   ");
        var firstVisit = await IndexModelAsync(controller, null);

        Assert.Equal(LoanLookupController.EmptyCodeMessage, empty.ValidationMessage);
        Assert.Null(empty.Result);
        Assert.Null(firstVisit.ValidationMessage);
        Assert.Null(firstVisit.Result);
    }

    [Fact]
    public async Task PageReportsSystemErrorsSeparatelyFromNoResults()
    {
        var controller = new LoanLookupController(new FailingLookupService(), NullLogger<LoanLookupController>.Instance);

        var model = await IndexModelAsync(controller, "CARD-001");

        Assert.Equal(LoanLookupController.SystemErrorMessage, model.ErrorMessage);
        Assert.Null(model.Result);
    }

    [Fact]
    public async Task ChangingPageKeepsTheCodeAndANewSearchStartsAtPageOne()
    {
        var first = await AddReaderAsync("CARD-001");
        var second = await AddReaderAsync("CARD-002");
        await AddLoansAsync(first, 21);
        await AddLoansAsync(second, 21);
        var controller = CreateController();

        var pageTwo = await IndexModelAsync(controller, " CARD-001 ", page: 2);
        Assert.Equal("CARD-001", pageTwo.Code);
        Assert.Equal("CARD-001", pageTwo.Result!.Code);
        Assert.Equal(2, pageTwo.Result.Page);
        Assert.All(pageTwo.Result.Items, item => Assert.Equal("CARD-001", item.CardCode));

        // Ô tìm kiếm gửi form GET chỉ có q (không kèm page) nên tìm mã mới quay về trang 1.
        var newSearch = await IndexModelAsync(controller, "CARD-002");
        Assert.Equal(1, newSearch.Result!.Page);
        Assert.All(newSearch.Result.Items, item => Assert.Equal("CARD-002", item.CardCode));
    }

    [Fact]
    public void PagerLinksAndSearchFormCarryTheExpectedParameters()
    {
        var view = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "LoanLookup", "Index.cshtml"));

        Assert.Contains("method=\"get\"", view);
        Assert.Contains("name=\"q\"", view);
        Assert.Contains("placeholder=\"Nhập mã thẻ, mã vạch bản sao hoặc mã phiếu mượn\"", view);
        Assert.Contains(">Tìm kiếm</button>", view);
        Assert.Contains("Không tìm thấy phiếu mượn phù hợp.", view);
        Assert.DoesNotContain("name=\"page\"", view);
        Assert.Equal(3, CountOccurrences(view, "asp-route-q=\"@result.Code\""));
        Assert.Equal(3, CountOccurrences(view, "asp-route-page="));
    }

    [Fact]
    public async Task ApiReturnsPagingInformationAndRejectsEmptyCode()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoansAsync(reader, 21);
        var controller = CreateController();

        Assert.IsType<BadRequestObjectResult>(await controller.SearchApi("  "));
        var ok = Assert.IsType<OkObjectResult>(await controller.SearchApi("CARD-001", 2));
        var json = System.Text.Json.JsonSerializer.SerializeToElement(ok.Value);
        Assert.Equal(21, json.GetProperty("totalItems").GetInt32());
        Assert.Equal(2, json.GetProperty("page").GetInt32());
        Assert.Equal(20, json.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, json.GetProperty("totalPages").GetInt32());
        Assert.Equal(1, json.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void LookupIsLimitedToLibraryStaffAndLoanScreenPermissionsAreUnchanged()
    {
        var lookup = typeof(LoanLookupController).GetCustomAttribute<StaffOnlyAttribute>()!;
        Assert.Equal(
            new[] { AccountRoles.Librarian, AccountRoles.LibraryManager, AccountRoles.SystemAdmin }.Order(),
            lookup.Roles.Order());

        var loans = typeof(LoanController).GetCustomAttribute<StaffOnlyAttribute>()!;
        Assert.Equal(new[] { AccountRoles.LibraryManager, AccountRoles.SystemAdmin }.Order(), loans.Roles.Order());
    }

    [Fact]
    public async Task AnonymousVisitorIsSentToStaffLogin()
    {
        var (result, ranAction) = await RunLookupFilterAsync("/LoanLookup", cookie: null);

        Assert.False(ranAction);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(("Account", "Login"), (redirect.ControllerName, redirect.ActionName));
    }

    [Fact]
    public async Task AnonymousApiCallIsUnauthorized()
    {
        var (result, ranAction) = await RunLookupFilterAsync("/api/loan-lookup", cookie: null);

        Assert.False(ranAction);
        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task ReaderSessionCannotUseTheLookup()
    {
        var (result, ranAction) = await RunLookupFilterAsync("/api/loan-lookup", cookie: "reader_session=fake");

        Assert.False(ranAction);
        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task DeactivatedStaffAccountIsRejected()
    {
        var cookie = await SignInStaffAsync(AccountRoles.Librarian);
        await db.AdminAccounts.ExecuteUpdateAsync(setters => setters.SetProperty(account => account.IsActive, false));

        var (_, ranAction) = await RunLookupFilterAsync("/LoanLookup", cookie);

        Assert.False(ranAction);
    }

    [Theory]
    [InlineData(AccountRoles.Librarian)]
    [InlineData(AccountRoles.LibraryManager)]
    [InlineData(AccountRoles.SystemAdmin)]
    public async Task LibraryStaffCanUseTheLookup(string role)
    {
        var (result, ranAction) = await RunLookupFilterAsync("/LoanLookup", await SignInStaffAsync(role));

        Assert.True(ranAction);
        Assert.Null(result);
    }

    private LoanLookupController CreateController() => new(service, NullLogger<LoanLookupController>.Instance);

    private static async Task<LoanLookupViewModel> IndexModelAsync(LoanLookupController controller, string? code, int page = 1)
    {
        var view = Assert.IsType<ViewResult>(await controller.Index(code, page));
        return Assert.IsType<LoanLookupViewModel>(view.Model);
    }

    private async Task<(IActionResult? Result, bool RanAction)> RunLookupFilterAsync(string path, string? cookie)
    {
        var auditLogService = new AuditLogService(db, NullLogger<AuditLogService>.Instance);
        var services = new ServiceCollection().AddSingleton<IAuditLogService>(auditLogService).BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Request.Path = path;
        httpContext.Request.Method = "GET";
        if (cookie is not null) httpContext.Request.Headers.Cookie = cookie;

        var context = new ActionExecutingContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor { EndpointMetadata = [] }),
            [], new Dictionary<string, object?>(), controller: new object());

        var ranAction = false;
        var filter = typeof(LoanLookupController).GetCustomAttribute<StaffOnlyAttribute>()!;
        await filter.OnActionExecutionAsync(context, () =>
        {
            ranAction = true;
            return Task.FromResult(new ActionExecutedContext(context, [], controller: new object()));
        });
        return (context.Result, ranAction);
    }

    private async Task<string> SignInStaffAsync(string role)
    {
        var account = new AdminAccount { Email = $"{role}-{Guid.NewGuid():N}@example.com", Role = role, IsActive = true, PasswordHash = "x" };
        var token = Guid.NewGuid().ToString("N");
        db.RefreshTokens.Add(new RefreshToken
        {
            AdminAccount = account,
            TokenHash = TokenService.HashRefreshToken(token),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();
        return $"admin_refresh={token}";
    }

    private async Task<Book> GetBookAsync()
    {
        if (book is not null) return book;
        book = new Book { Title = "Sách tra cứu", Author = new Author { Name = "Tác giả tra cứu" } };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book;
    }

    private async Task<ReaderAccount> AddReaderAsync(string cardCode, string? fullName = null)
    {
        var cardType = await db.LibraryCardTypes.FirstOrDefaultAsync() ?? new LibraryCardType { Name = "Standard" };
        if (cardType.Id == 0)
        {
            db.LibraryCardTypes.Add(cardType);
            await db.SaveChangesAsync();
        }
        var sequence = ++readerSequence;
        var reader = new ReaderAccount
        {
            FullName = fullName ?? $"Bạn đọc {sequence}", DateOfBirth = new DateOnly(2000, 1, 1),
            Email = $"reader{sequence}@example.com", PhoneNumber = "0900000000", StudentOrStaffCode = $"SV{sequence}",
            PasswordHash = "x", Status = "Đang hoạt động",
            LibraryCard = new LibraryCard
            {
                CardCode = cardCode, LibraryCardTypeId = cardType.Id, Status = "Đang hoạt động",
                IssuedOn = BaseDate.AddYears(-1), ExpiresOn = BaseDate.AddYears(1)
            }
        };
        db.ReaderAccounts.Add(reader);
        await db.SaveChangesAsync();
        return reader;
    }

    private async Task SetCardCodeAsync(ReaderAccount reader, string cardCode)
    {
        await db.LibraryCards.Where(card => card.ReaderAccountId == reader.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(card => card.CardCode, cardCode));
    }

    private async Task<BookLoan> AddLoanAsync(ReaderAccount reader, DateOnly loanDate)
    {
        var loan = new BookLoan
        {
            BookId = (await GetBookAsync()).Id, ReaderAccountId = reader.Id, LoanDate = loanDate,
            OriginalDueDate = loanDate.AddDays(14), DueDate = loanDate.AddDays(14)
        };
        db.BookLoans.Add(loan);
        await db.SaveChangesAsync();
        return loan;
    }

    private async Task<List<BookLoan>> AddLoansAsync(ReaderAccount reader, int count)
    {
        var loans = new List<BookLoan>();
        for (var index = 0; index < count; index++) loans.Add(await AddLoanAsync(reader, BaseDate.AddDays(index)));
        return loans;
    }

    private async Task AddCopyAsync(int bookId, string copyCode)
    {
        var shelf = await db.Shelves.FirstOrDefaultAsync()
            ?? new Shelf { Warehouse = new Warehouse { Code = "WH-1", Name = "Kho" }, Code = "SH-1", Name = "Kệ" };
        db.BookCopies.Add(new BookCopy { BookId = bookId, Shelf = shelf, CopyCode = copyCode, Status = BookCopyStatus.OnLoan });
        await db.SaveChangesAsync();
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Project.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Không tìm thấy thư mục dự án.");
    }

    private sealed class FailingLookupService : ILoanLookupService
    {
        public Task<LoanLookupPage?> SearchAsync(string? code, int page, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Mất kết nối cơ sở dữ liệu.");
    }
}
