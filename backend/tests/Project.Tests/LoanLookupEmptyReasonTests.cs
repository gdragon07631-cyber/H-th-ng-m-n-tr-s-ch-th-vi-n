using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Project.Controllers;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Tests;

public sealed class LoanLookupEmptyReasonTests : IDisposable
{
    private static readonly DateOnly Sep1 = new(2026, 9, 1);
    private static readonly DateOnly Sep5 = new(2026, 9, 5);
    private static readonly DateOnly Sep10 = new(2026, 9, 10);
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly CommandCounter commands = new();
    private readonly ApplicationDbContext db;
    private readonly LoanLookupService service;
    private Book? book;
    private int readerSequence;

    public LoanLookupEmptyReasonTests()
    {
        connection.Open();
        db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).AddInterceptors(commands).Options);
        db.Database.EnsureCreated();
        service = new LoanLookupService(db);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    // ---------- Phân biệt nguyên nhân ----------

    [Theory]
    [InlineData("KHONG-TON-TAI")]
    [InlineData("#999999")]
    [InlineData("999999")]
    public async Task CodeWithoutLoansIsNoMatchingCode(string code)
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoanAsync(reader, Sep5);

        var result = await service.SearchAsync(code, 1);

        Assert.Equal(0, result!.TotalItems);
        Assert.Equal(LoanLookupEmptyReason.NoMatchingCode, result.EmptyReason);
    }

    [Fact]
    public async Task CodeWithoutLoansIsNoMatchingCodeEvenWhenFiltersAreApplied()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoanAsync(reader, Sep5);

        var result = await service.SearchAsync("KHONG-TON-TAI", 1, new LoanLookupFilter(Sep1, Sep10, LoanLookupStatus.Open));

        Assert.Equal(LoanLookupEmptyReason.NoMatchingCode, result!.EmptyReason);
    }

    [Fact]
    public async Task DateRangeExcludingEveryLoanOfTheCardIsFilteredOut()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoanAsync(reader, Sep1);

        var result = await service.SearchAsync("CARD-001", 1, new LoanLookupFilter(Sep5, Sep10));

        Assert.Equal(0, result!.TotalItems);
        Assert.Equal(LoanLookupEmptyReason.FilteredOut, result.EmptyReason);
    }

    [Fact]
    public async Task DateAndStatusTogetherExcludingEveryLoanIsFilteredOut()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoanAsync(reader, Sep1);

        var result = await service.SearchAsync("CARD-001", 1, new LoanLookupFilter(From: Sep5, Status: LoanLookupStatus.Open));

        Assert.Equal(LoanLookupEmptyReason.FilteredOut, result!.EmptyReason);
    }

    [Fact]
    public async Task StatusFilterAloneCannotEmptyTheListWithTodaysStatuses()
    {
        // Mọi phiếu còn bản ghi đều "Đang mượn" — trạng thái duy nhất được lọc — nên lọc riêng trạng thái không bao
        // giờ loại hết phiếu; khi có kết quả thì không trả nguyên nhân.
        var reader = await AddReaderAsync("CARD-001");
        await AddLoanAsync(reader, Sep1);

        var result = await service.SearchAsync("CARD-001", 1, new LoanLookupFilter(Status: LoanLookupStatus.Open));

        Assert.Equal(1, result!.TotalItems);
        Assert.Null(result.EmptyReason);
    }

    [Fact]
    public async Task LoanCodeBranchDistinguishesBothReasons()
    {
        var reader = await AddReaderAsync("CARD-001");
        var loan = await AddLoanAsync(reader, Sep1);

        var filteredOut = await service.SearchAsync($"#{loan.Id}", 1, new LoanLookupFilter(From: Sep5));
        var noMatch = await service.SearchAsync($"#{loan.Id + 1000}", 1, new LoanLookupFilter(From: Sep5));

        Assert.Equal(LoanLookupEmptyReason.FilteredOut, filteredOut!.EmptyReason);
        Assert.Equal(LoanLookupEmptyReason.NoMatchingCode, noMatch!.EmptyReason);
    }

    [Fact]
    public async Task CopyBarcodeBranchUsesTheSameMatchingRulesAsTheMainQuery()
    {
        var reader = await AddReaderAsync("SHARED-01");
        var loan = await AddLoanAsync(reader, Sep1);
        await AddCopyAsync(loan.BookId, "BC-ONLY");
        await AddCopyAsync(loan.BookId, "SHARED-01");

        // Mã vạch chưa liên kết phiếu (giới hạn từ Lát 1) → bỏ bộ lọc cũng không có phiếu → nguyên nhân là mã.
        var barcodeOnly = await service.SearchAsync("BC-ONLY", 1, new LoanLookupFilter(From: Sep5));
        // Mã trùng cả mã vạch lẫn mã thẻ: nhánh mã thẻ có phiếu → bộ lọc loại hết.
        var shared = await service.SearchAsync("SHARED-01", 1, new LoanLookupFilter(From: Sep5));

        Assert.Equal(LoanLookupEmptyReason.NoMatchingCode, barcodeOnly!.EmptyReason);
        Assert.Equal(LoanLookupEmptyReason.FilteredOut, shared!.EmptyReason);
    }

    [Fact]
    public async Task LoansThatDoNotMatchTheCodeNeverChangeTheReason()
    {
        // Phiếu của bạn đọc khác (không khớp mã) nằm trong khoảng ngày không được làm mã này thành "bộ lọc loại hết".
        var searched = await AddReaderAsync("CARD-EMPTY");
        var other = await AddReaderAsync("CARD-OTHER");
        await AddLoanAsync(other, Sep1);
        await AddLoanAsync(other, Sep5);

        var result = await service.SearchAsync("CARD-EMPTY", 1, new LoanLookupFilter(Sep5, Sep10));

        Assert.NotEqual(0, searched.Id);
        Assert.Equal(LoanLookupEmptyReason.NoMatchingCode, result!.EmptyReason);
    }

    [Fact]
    public async Task ResultsAndOutOfRangePagesCarryNoReason()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoansAsync(reader, 21, Sep1);

        var firstPage = await service.SearchAsync("CARD-001", 1, new LoanLookupFilter(From: Sep1));
        var beyondLastPage = await service.SearchAsync("CARD-001", 99, new LoanLookupFilter(From: Sep1));

        Assert.Null(firstPage!.EmptyReason);
        Assert.Equal(2, beyondLastPage!.Page);
        Assert.Null(beyondLastPage.EmptyReason);
    }

    [Fact]
    public async Task ExistenceCheckRunsOnlyWhenFiltersMightHaveEmptiedTheList()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoanAsync(reader, Sep1);

        Assert.Equal(2, await CountCommandsAsync(() => service.SearchAsync("CARD-001", 1, new LoanLookupFilter(From: Sep1))));
        Assert.Equal(2, await CountCommandsAsync(() => service.SearchAsync("KHONG-TON-TAI", 1)));
        Assert.Equal(3, await CountCommandsAsync(() => service.SearchAsync("CARD-001", 1, new LoanLookupFilter(From: Sep5))));
    }

    [Fact]
    public async Task ExistingFieldsAreUnchangedByTheReason()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoanAsync(reader, Sep1);

        var result = await service.SearchAsync("CARD-001", 3, new LoanLookupFilter(From: Sep5));

        Assert.Equal(("CARD-001", 0, 1, 20, 0), (result!.Code, result.TotalItems, result.Page, result.PageSize, result.TotalPages));
        Assert.Empty(result.Items);
        Assert.Equal(new LoanLookupFilter(From: Sep5), result.Filter);
    }

    // ---------- Màn hình ----------

    [Fact]
    public async Task EditingTheCodeAndSearchingAgainReplacesTheSuggestionWithResults()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoanAsync(reader, Sep1);
        var controller = CreateController();

        var wrong = await ModelAsync(controller.Index("CARD-00l"));
        Assert.Equal(LoanLookupEmptyReason.NoMatchingCode, wrong.Result!.EmptyReason);
        Assert.Equal("CARD-00l", wrong.Code);  // mã sai được giữ trong ô để sửa

        var redirect = Assert.IsType<RedirectToActionResult>(await controller.Index("CARD-001", op: LoanLookupController.SearchOperation));
        var corrected = await ModelAsync(controller.Index((string)redirect.RouteValues!["q"]!));
        Assert.Equal(1, corrected.Result!.TotalItems);
        Assert.Null(corrected.Result.EmptyReason);
    }

    [Fact]
    public async Task ClearingFiltersFromTheSuggestionRestoresTheListForTheSameCode()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoansAsync(reader, 21, Sep1);
        var controller = CreateController();

        var filteredOut = await ModelAsync(controller.Index("CARD-001", page: 2, from: "2026-12-01", status: LoanLookupStatus.Open));
        Assert.Equal(LoanLookupEmptyReason.FilteredOut, filteredOut.Result!.EmptyReason);

        // Nút trong thông báo gửi q + op=clear (đúng thao tác Xóa bộ lọc của Lát 2).
        var redirect = Assert.IsType<RedirectToActionResult>(
            await controller.Index(filteredOut.Result.Code, op: LoanLookupController.ClearFilterOperation));
        Assert.Equal(new[] { "q" }, redirect.RouteValues!.Keys);
        var restored = await ModelAsync(controller.Index((string)redirect.RouteValues["q"]!));

        Assert.Equal("CARD-001", restored.Code);
        Assert.Equal(1, restored.Result!.Page);
        Assert.Equal(21, restored.Result.TotalItems);
        Assert.Null(restored.Result.EmptyReason);
        Assert.True(restored.Result.Filter.IsEmpty);
    }

    [Fact]
    public async Task NonSearchStatesHaveNoResultSoNoSuggestionIsShown()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoanAsync(reader, Sep1);
        var controller = CreateController();
        var failing = new LoanLookupController(new FailingLookupService(), NullLogger<LoanLookupController>.Instance);

        var notSearched = await ModelAsync(controller.Index(null));
        var emptyCode = await ModelAsync(controller.Index("  ", from: "2026-09-05"));
        var reversed = await ModelAsync(controller.Index("KHONG-TON-TAI", from: "2026-09-10", to: "2026-09-01"));
        var systemError = await ModelAsync(failing.Index("KHONG-TON-TAI", from: "2026-09-05"));

        foreach (var model in new[] { notSearched, emptyCode, reversed, systemError }) Assert.Null(model.Result);
        Assert.Equal(LoanLookupController.EmptyCodeMessage, emptyCode.ValidationMessage);
        Assert.Equal(LoanLookupFilter.ReversedRangeMessage, reversed.FilterError);
        Assert.Equal(LoanLookupController.SystemErrorMessage, systemError.ErrorMessage);
    }

    [Fact]
    public async Task ApiAddsEmptyReasonWithoutChangingExistingFields()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoanAsync(reader, Sep1);
        var controller = CreateController();

        static System.Text.Json.JsonElement Json(IActionResult result) =>
            System.Text.Json.JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value);

        var noMatch = Json(await controller.SearchApi("KHONG-TON-TAI"));
        var filteredOut = Json(await controller.SearchApi("CARD-001", from: "2026-09-05"));
        var found = Json(await controller.SearchApi("CARD-001"));

        Assert.Equal(LoanLookupEmptyReasonCodes.NoMatchingCode, noMatch.GetProperty("emptyReason").GetString());
        Assert.Equal(LoanLookupEmptyReasonCodes.FilteredOut, filteredOut.GetProperty("emptyReason").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, found.GetProperty("emptyReason").ValueKind);
        foreach (var name in new[] { "code", "items", "totalItems", "page", "pageSize", "totalPages", "from", "to", "status" })
            Assert.True(found.TryGetProperty(name, out _), name);
    }

    [Fact]
    public void ViewShowsTheAgreedSuggestionsAndReusesClearFilters()
    {
        var view = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "..", "frontend", "Views", "LoanLookup", "Index.cshtml"));

        foreach (var text in new[]
                 {
                     "Không tìm thấy phiếu mượn phù hợp với mã đã nhập", "Vui lòng kiểm tra lại:",
                     "<li>Mã thẻ bạn đọc.</li>", "<li>Mã vạch bản sao sách.</li>", "<li>Mã phiếu mượn.</li>",
                     "Kiểm tra loại mã và giá trị đã nhập, sau đó tìm lại.",
                     "Mã đã nhập có phiếu mượn, nhưng không có phiếu phù hợp với khoảng ngày hoặc trạng thái đang chọn. Hãy kiểm tra bộ lọc hoặc xóa bộ lọc để xem lại kết quả.",
                     "<input type=\"hidden\" name=\"q\" value=\"@result.Code\" />",
                     "value=\"@LoanLookupController.ClearFilterOperation\">Xóa bộ lọc</button>"
                 })
            Assert.Contains(text, view);
        // Gợi ý chỉ nằm trong nhánh "không có kết quả"; mã nhập được xuất bằng @ (Razor tự escape), không dùng Html.Raw.
        Assert.True(view.IndexOf("result.TotalItems == 0", StringComparison.Ordinal) < view.IndexOf("loanLookupNoMatch", StringComparison.Ordinal));
        Assert.DoesNotContain("Html.Raw", view);
        Assert.Equal(2, CountOccurrences(view, "value=\"@LoanLookupController.ClearFilterOperation\""));
    }

    // ---------- Helpers ----------

    private LoanLookupController CreateController() => new(service, NullLogger<LoanLookupController>.Instance);

    private async Task<int> CountCommandsAsync(Func<Task> action)
    {
        commands.Count = 0;
        await action();
        return commands.Count;
    }

    private static async Task<LoanLookupViewModel> ModelAsync(Task<IActionResult> action)
    {
        var view = Assert.IsType<ViewResult>(await action);
        return Assert.IsType<LoanLookupViewModel>(view.Model);
    }

    private async Task<Book> GetBookAsync()
    {
        if (book is not null) return book;
        book = new Book { Title = "Sách gợi ý", Author = new Author { Name = "Tác giả gợi ý" } };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book;
    }

    private async Task<ReaderAccount> AddReaderAsync(string cardCode)
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
            FullName = $"Bạn đọc {sequence}", DateOfBirth = new DateOnly(2000, 1, 1),
            Email = $"empty{sequence}@example.com", PhoneNumber = "0900000000", StudentOrStaffCode = $"SV{sequence}",
            PasswordHash = "x", Status = "Đang hoạt động",
            LibraryCard = new LibraryCard
            {
                CardCode = cardCode, LibraryCardTypeId = cardType.Id, Status = "Đang hoạt động",
                IssuedOn = Sep1.AddYears(-1), ExpiresOn = Sep1.AddYears(1)
            }
        };
        db.ReaderAccounts.Add(reader);
        await db.SaveChangesAsync();
        return reader;
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

    private async Task AddLoansAsync(ReaderAccount reader, int count, DateOnly start)
    {
        for (var index = 0; index < count; index++) await AddLoanAsync(reader, start.AddDays(index));
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

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class FailingLookupService : ILoanLookupService
    {
        public Task<LoanLookupPage?> SearchAsync(string? code, int page, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Mất kết nối cơ sở dữ liệu.");

        public Task<LoanLookupPage?> SearchAsync(string? code, int page, LoanLookupFilter filter, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Mất kết nối cơ sở dữ liệu.");
    }
}
