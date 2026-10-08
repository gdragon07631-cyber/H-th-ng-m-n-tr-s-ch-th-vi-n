using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Project.Controllers;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Tests;

public sealed class LoanLookupFilterTests : IDisposable
{
    private static readonly DateOnly Sep1 = new(2026, 9, 1);
    private static readonly DateOnly Sep5 = new(2026, 9, 5);
    private static readonly DateOnly Sep10 = new(2026, 9, 10);
    private static readonly DateOnly Sep11 = new(2026, 9, 11);
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly ApplicationDbContext db;
    private readonly LoanLookupService service;
    private Book? book;
    private int readerSequence;

    public LoanLookupFilterTests()
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

    // ---------- Service ----------

    [Fact]
    public async Task DateRangeIncludesBothBoundaryDaysAndExcludesTheDayAfter()
    {
        var reader = await AddReaderAsync("CARD-001");
        var before = await AddLoanAsync(reader, Sep1.AddDays(3));
        var onFrom = await AddLoanAsync(reader, Sep5);
        var inside = await AddLoanAsync(reader, Sep5.AddDays(2));
        var onTo = await AddLoanAsync(reader, Sep10);
        var dayAfterTo = await AddLoanAsync(reader, Sep11);

        var result = await service.SearchAsync("CARD-001", 1, new LoanLookupFilter(Sep5, Sep10));

        Assert.Equal([onTo.Id, inside.Id, onFrom.Id], Ids(result));
        Assert.Equal(3, result!.TotalItems);
        Assert.DoesNotContain(before.Id, Ids(result));
        Assert.DoesNotContain(dayAfterTo.Id, Ids(result));
    }

    [Fact]
    public void LoanDateHasNoTimePartSoDayBoundariesAreExact()
    {
        // LoanDate là DateOnly lưu cột "date": không có giờ/UTC, nên "< đầu ngày sau Đến ngày" ≡ "≤ Đến ngày".
        var property = db.Model.FindEntityType(typeof(BookLoan))!.FindProperty(nameof(BookLoan.LoanDate))!;
        Assert.Equal(typeof(DateOnly), property.ClrType);
        Assert.Equal("date", property.GetColumnType());
    }

    [Fact]
    public async Task OnlyFromDateKeepsLoansFromThatDayOnward()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoanAsync(reader, Sep1);
        var onFrom = await AddLoanAsync(reader, Sep5);
        var later = await AddLoanAsync(reader, Sep11);

        var result = await service.SearchAsync("CARD-001", 1, new LoanLookupFilter(From: Sep5));

        Assert.Equal([later.Id, onFrom.Id], Ids(result));
    }

    [Fact]
    public async Task OnlyToDateKeepsLoansUpToTheEndOfThatDay()
    {
        var reader = await AddReaderAsync("CARD-001");
        var earlier = await AddLoanAsync(reader, Sep1);
        var onTo = await AddLoanAsync(reader, Sep5);
        await AddLoanAsync(reader, Sep5.AddDays(1));

        var result = await service.SearchAsync("CARD-001", 1, new LoanLookupFilter(To: Sep5));

        Assert.Equal([onTo.Id, earlier.Id], Ids(result));
    }

    [Fact]
    public async Task StatusFilterUsesTheExistingOpenStatus()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoansAsync(reader, 3, Sep1);

        var all = await service.SearchAsync("CARD-001", 1, LoanLookupFilter.None);
        var open = await service.SearchAsync("CARD-001", 1, new LoanLookupFilter(Status: LoanLookupStatus.Open));

        Assert.Equal(Ids(all), Ids(open));
        Assert.All(open!.Items, item => Assert.Equal(LoanLookupStatus.Open, item.Status));
        Assert.Equal([LoanLookupStatus.Open], LoanLookupStatus.All);
    }

    [Fact]
    public async Task DateAndStatusFiltersApplyToTheCardCodeBranch()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoanAsync(reader, Sep1);
        var match = await AddLoanAsync(reader, Sep5);

        var result = await service.SearchAsync("CARD-001", 1, new LoanLookupFilter(Sep5, Sep10, LoanLookupStatus.Open));

        Assert.Equal([match.Id], Ids(result));
    }

    [Fact]
    public async Task DateAndStatusFiltersApplyToTheLoanCodeBranch()
    {
        var reader = await AddReaderAsync("CARD-001");
        var loan = await AddLoanAsync(reader, Sep5);
        var filter = new LoanLookupFilter(Sep5, Sep10, LoanLookupStatus.Open);

        Assert.Equal([loan.Id], Ids(await service.SearchAsync($"#{loan.Id}", 1, filter)));
        Assert.Equal(0, (await service.SearchAsync($"#{loan.Id}", 1, new LoanLookupFilter(From: Sep11)))!.TotalItems);
    }

    [Fact]
    public async Task CopyBarcodeStillMatchesNoLoanWhenFiltered()
    {
        var reader = await AddReaderAsync("CARD-001");
        var loan = await AddLoanAsync(reader, Sep5);
        await AddCopyAsync(loan.BookId, "BC-0001");

        var result = await service.SearchAsync("BC-0001", 1, new LoanLookupFilter(Sep1, Sep10, LoanLookupStatus.Open));

        Assert.Equal(0, result!.TotalItems);
    }

    [Fact]
    public async Task CodeMatchingCardAndBarcodeIsFilteredWithoutDuplicates()
    {
        var reader = await AddReaderAsync("SHARED-01");
        await AddLoanAsync(reader, Sep1);
        var inside = await AddLoanAsync(reader, Sep5);
        await AddCopyAsync(inside.BookId, "SHARED-01");

        var result = await service.SearchAsync("SHARED-01", 1, new LoanLookupFilter(From: Sep5));

        Assert.Equal([inside.Id], Ids(result));
        Assert.Equal(1, result!.TotalItems);
    }

    [Fact]
    public async Task CodeMatchingLoanIdAndCardIsMergedOnceAndFilteredOnEveryBranch()
    {
        var owner = await AddReaderAsync("TEMP-A");
        var someoneElse = await AddReaderAsync("TEMP-B");
        var ownOld = await AddLoanAsync(owner, Sep1);
        var otherLoan = await AddLoanAsync(someoneElse, Sep10);
        var ownNew = await AddLoanAsync(owner, Sep5);
        await SetCardCodeAsync(owner, otherLoan.Id.ToString());

        var everything = await service.SearchAsync(otherLoan.Id.ToString(), 1, new LoanLookupFilter(Sep1, Sep10));
        Assert.Equal([otherLoan.Id, ownNew.Id, ownOld.Id], Ids(everything));

        // Khoảng ngày loại phiếu của nhánh mã phiếu (10/9) và một phiếu của nhánh mã thẻ (1/9).
        var narrowed = await service.SearchAsync(otherLoan.Id.ToString(), 1, new LoanLookupFilter(Sep5, Sep5));
        Assert.Equal([ownNew.Id], Ids(narrowed));
        Assert.Equal(1, narrowed!.TotalItems);

        // Thẻ trùng mã phiếu của chính chủ thẻ: phiếu đó chỉ xuất hiện một lần sau lọc.
        await SetCardCodeAsync(owner, ownNew.Id.ToString());
        var self = await service.SearchAsync(ownNew.Id.ToString(), 1, new LoanLookupFilter(From: Sep5));
        Assert.Equal([ownNew.Id], Ids(self));
    }

    [Fact]
    public async Task FilteredResultsKeepTheLoanDateThenIdDescendingOrder()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoanAsync(reader, Sep1);
        var sameDayFirst = await AddLoanAsync(reader, Sep5);
        var newest = await AddLoanAsync(reader, Sep10);
        var sameDaySecond = await AddLoanAsync(reader, Sep5);

        var result = await service.SearchAsync("CARD-001", 1, new LoanLookupFilter(Sep5, Sep10, LoanLookupStatus.Open));

        Assert.Equal([newest.Id, sameDaySecond.Id, sameDayFirst.Id], Ids(result));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 1)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    public async Task PagingAndTotalsCountOnlyLoansLeftAfterFiltering(int matching, int expectedOnFirstPage, int expectedPages)
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoansAsync(reader, 5, Sep1.AddMonths(-2));   // ngoài khoảng, trước Từ ngày
        await AddLoansAsync(reader, matching, Sep1);
        await AddLoansAsync(reader, 5, Sep1.AddMonths(2));    // ngoài khoảng, sau Đến ngày
        var filter = new LoanLookupFilter(Sep1, Sep1.AddMonths(1).AddDays(-1));

        var first = await service.SearchAsync("CARD-001", 1, filter);

        Assert.Equal(matching, first!.TotalItems);
        Assert.Equal(expectedOnFirstPage, first.Items.Count);
        Assert.Equal(expectedPages, first.TotalPages);
        Assert.All(first.Items, item => Assert.InRange(item.LoanDate, filter.From!.Value, filter.To!.Value));
        if (matching == 21)
        {
            var second = await service.SearchAsync("CARD-001", 2, filter);
            Assert.Single(second!.Items);
            Assert.Equal(Sep1, second.Items[0].LoanDate);
            Assert.Equal(21, Ids(first).Concat(Ids(second)).Distinct().Count());
        }
    }

    [Fact]
    public async Task NoFilterGivesExactlyTheSliceOneResult()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoansAsync(reader, 23, Sep1);

        for (var page = 1; page <= 2; page++)
        {
            var sliceOne = await service.SearchAsync("CARD-001", page);
            var unfiltered = await service.SearchAsync("CARD-001", page, LoanLookupFilter.None);
            Assert.Equal(sliceOne!.Items, unfiltered!.Items);
            Assert.Equal((sliceOne.TotalItems, sliceOne.Page, sliceOne.TotalPages), (unfiltered.TotalItems, unfiltered.Page, unfiltered.TotalPages));
            Assert.True(sliceOne.Filter.IsEmpty);
        }
    }

    [Fact]
    public async Task ServiceRejectsReversedRangeAndUnknownStatus()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoanAsync(reader, Sep5);

        var reversed = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SearchAsync("CARD-001", 1, new LoanLookupFilter(Sep10, Sep1)));
        Assert.StartsWith(LoanLookupFilter.ReversedRangeMessage, reversed.Message);
        var status = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SearchAsync("CARD-001", 1, new LoanLookupFilter(Status: "Đã trả")));
        Assert.StartsWith(LoanLookupFilter.InvalidStatusMessage, status.Message);
    }

    [Fact]
    public async Task FiltersAloneNeverReturnEveryLoan()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoanAsync(reader, Sep5);

        Assert.Null(await service.SearchAsync("  ", 1, new LoanLookupFilter(Sep1, Sep10, LoanLookupStatus.Open)));
    }

    [Theory]
    [InlineData("2026-09-10", "2026-09-01", null, LoanLookupFilter.ReversedRangeMessage)]
    [InlineData("2026-13-01", null, null, LoanLookupFilter.InvalidDateMessage)]
    [InlineData("10/09/2026", null, null, LoanLookupFilter.InvalidDateMessage)]
    [InlineData(null, null, "Đã trả", LoanLookupFilter.InvalidStatusMessage)]
    [InlineData(null, null, "đang mượn", LoanLookupFilter.InvalidStatusMessage)]
    public void ParsingRejectsInvalidInput(string? from, string? to, string? status, string expectedError)
    {
        Assert.Null(LoanLookupFilter.TryParse(from, to, status, out var error));
        Assert.Equal(expectedError, error);
    }

    [Fact]
    public void ParsingTreatsBlankValuesAsNoFilter()
    {
        var filter = LoanLookupFilter.TryParse(" ", "", "  ", out var error);

        Assert.Null(error);
        Assert.True(filter!.IsEmpty);
        Assert.Equal(new LoanLookupFilter(Sep5, Sep5, LoanLookupStatus.Open),
            LoanLookupFilter.TryParse("2026-09-05", "2026-09-05", LoanLookupStatus.Open, out _));
    }

    // ---------- Controller ----------

    [Fact]
    public void ApplyingFiltersFromPageTwoRedirectsToPageOneWithTheFilters()
    {
        var redirect = Redirect(CreateController().Index(" CARD-001 ", page: 2, from: "2026-09-05", to: "2026-09-10",
            status: LoanLookupStatus.Open, op: LoanLookupController.ApplyFilterOperation));

        AssertRoute(redirect, "CARD-001", "2026-09-05", "2026-09-10", LoanLookupStatus.Open);
        Assert.False(redirect.RouteValues!.ContainsKey("page"));
    }

    [Fact]
    public async Task ReversedRangeIsReportedKeepsTheInputAndDoesNotQuery()
    {
        var counting = new CountingLookupService(service);
        var controller = new LoanLookupController(counting, NullLogger<LoanLookupController>.Instance);

        var applied = await ModelAsync(controller.Index("CARD-001", from: "2026-09-10", to: "2026-09-01",
            status: LoanLookupStatus.Open, op: LoanLookupController.ApplyFilterOperation, appliedFrom: "2026-09-01"));
        var viaUrl = await ModelAsync(controller.Index("CARD-001", from: "2026-09-10", to: "2026-09-01"));

        foreach (var model in new[] { applied, viaUrl })
        {
            Assert.Equal(LoanLookupFilter.ReversedRangeMessage, model.FilterError);
            Assert.Equal(("CARD-001", "2026-09-10", "2026-09-01"), (model.Code, model.From, model.To));
            Assert.Null(model.Result);
        }
        Assert.Equal(LoanLookupStatus.Open, applied.Status);
        Assert.Equal("2026-09-01", applied.AppliedFrom);
        Assert.Equal(0, counting.Calls);
    }

    [Fact]
    public async Task InvalidStatusIsReportedWithoutQuerying()
    {
        var counting = new CountingLookupService(service);
        var controller = new LoanLookupController(counting, NullLogger<LoanLookupController>.Instance);

        var model = await ModelAsync(controller.Index("CARD-001", status: "Đã trả"));

        Assert.Equal(LoanLookupFilter.InvalidStatusMessage, model.FilterError);
        Assert.Equal(0, counting.Calls);
    }

    [Fact]
    public async Task PagingKeepsTheCodeAndTheAppliedFilters()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoansAsync(reader, 21, Sep1);
        await AddLoansAsync(reader, 4, Sep1.AddMonths(-3));

        var model = await ModelAsync(CreateController().Index("CARD-001", page: 2, from: "2026-09-01", status: LoanLookupStatus.Open));

        Assert.Equal(2, model.Result!.Page);
        Assert.Equal(21, model.Result.TotalItems);
        Assert.Single(model.Result.Items);
        Assert.Equal(new LoanLookupFilter(From: Sep1, Status: LoanLookupStatus.Open), model.Result.Filter);
        Assert.Equal(("2026-09-01", null, LoanLookupStatus.Open), (model.AppliedFrom, model.AppliedTo, model.AppliedStatus));
    }

    [Fact]
    public void NewSearchUsesTheAppliedFiltersNotTheUnappliedEdits()
    {
        var redirect = Redirect(CreateController().Index("CARD-002", page: 3, from: "2026-01-01", to: "2026-12-31",
            status: null, op: LoanLookupController.SearchOperation,
            appliedFrom: "2026-09-05", appliedTo: "2026-09-10", appliedStatus: LoanLookupStatus.Open));

        AssertRoute(redirect, "CARD-002", "2026-09-05", "2026-09-10", LoanLookupStatus.Open);
        Assert.False(redirect.RouteValues!.ContainsKey("page"));
    }

    [Fact]
    public void ClearingFiltersKeepsTheCodeAndReturnsToPageOne()
    {
        var redirect = Redirect(CreateController().Index(" CARD-001 ", page: 2, from: "2026-09-05", to: "2026-09-01",
            status: LoanLookupStatus.Open, op: LoanLookupController.ClearFilterOperation,
            appliedFrom: "2026-09-05", appliedStatus: LoanLookupStatus.Open));

        Assert.Equal(nameof(LoanLookupController.Index), redirect.ActionName);
        Assert.Equal(new[] { "q" }, redirect.RouteValues!.Keys);
        Assert.Equal("CARD-001", redirect.RouteValues["q"]);
    }

    [Fact]
    public async Task ClearedUrlGivesTheSameResultAsSliceOne()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoansAsync(reader, 3, Sep1);
        var controller = CreateController();

        var filtered = await ModelAsync(controller.Index("CARD-001", from: "2026-09-02"));
        var cleared = await ModelAsync(controller.Index("CARD-001"));

        Assert.Equal(2, filtered.Result!.TotalItems);
        Assert.Equal(3, cleared.Result!.TotalItems);
        Assert.True(cleared.Result.Filter.IsEmpty);
        Assert.Equal((null, null, null), (cleared.AppliedFrom, cleared.AppliedTo, cleared.AppliedStatus));
    }

    [Fact]
    public async Task FiltersWithoutACodeAskForACode()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoanAsync(reader, Sep5);
        var controller = CreateController();

        var viaUrl = await ModelAsync(controller.Index(null, from: "2026-09-01"));
        var viaApply = await ModelAsync(controller.Index("  ", from: "2026-09-01", op: LoanLookupController.ApplyFilterOperation));
        var viaSearch = await ModelAsync(controller.Index("", op: LoanLookupController.SearchOperation, appliedFrom: "2026-09-01"));

        foreach (var model in new[] { viaUrl, viaApply, viaSearch })
        {
            Assert.Equal(LoanLookupController.EmptyCodeMessage, model.ValidationMessage);
            Assert.Null(model.Result);
            Assert.Equal("2026-09-01", model.From);
        }
    }

    [Fact]
    public async Task FilteredSearchWithNoMatchKeepsTheSliceOneNotFoundState()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoanAsync(reader, Sep1);

        var model = await ModelAsync(CreateController().Index("CARD-001", from: "2026-09-05"));

        Assert.Equal(0, model.Result!.TotalItems);
        Assert.Null(model.ErrorMessage);
        Assert.Null(model.FilterError);
    }

    [Fact]
    public async Task ApiAppliesAndValidatesFilters()
    {
        var reader = await AddReaderAsync("CARD-001");
        await AddLoansAsync(reader, 21, Sep1);
        await AddLoanAsync(reader, Sep1.AddMonths(-1));
        var controller = CreateController();

        var reversed = Assert.IsType<BadRequestObjectResult>(await controller.SearchApi("CARD-001", from: "2026-09-10", to: "2026-09-01"));
        Assert.Equal(LoanLookupFilter.ReversedRangeMessage,
            System.Text.Json.JsonSerializer.SerializeToElement(reversed.Value).GetProperty("message").GetString());
        Assert.IsType<BadRequestObjectResult>(await controller.SearchApi("CARD-001", status: "Đã trả"));
        Assert.IsType<BadRequestObjectResult>(await controller.SearchApi(" ", from: "2026-09-01"));

        var ok = Assert.IsType<OkObjectResult>(await controller.SearchApi("CARD-001", 2, from: "2026-09-01", status: LoanLookupStatus.Open));
        var json = System.Text.Json.JsonSerializer.SerializeToElement(ok.Value);
        Assert.Equal(21, json.GetProperty("totalItems").GetInt32());
        Assert.Equal(2, json.GetProperty("totalPages").GetInt32());
        Assert.Equal(1, json.GetProperty("items").GetArrayLength());
        Assert.Equal("2026-09-01", json.GetProperty("from").GetString());
    }

    [Fact]
    public void ViewCarriesTheAppliedFiltersInPagingAndKeepsSearchAsTheDefaultButton()
    {
        var view = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "..", "frontend", "Views", "LoanLookup", "Index.cshtml"));

        foreach (var text in new[] { "Từ ngày", "Đến ngày", "Trạng thái", "Tất cả trạng thái", ">Áp dụng bộ lọc</button>", ">Xóa bộ lọc</button>",
                     "type=\"date\" name=\"from\"", "type=\"date\" name=\"to\"", "name=\"status\"",
                     "name=\"appliedFrom\"", "name=\"appliedTo\"", "name=\"appliedStatus\"", "loan-lookup-filters.js" })
            Assert.Contains(text, view);
        foreach (var route in new[] { "asp-route-from=\"@appliedFilter.FromText\"", "asp-route-to=\"@appliedFilter.ToText\"", "asp-route-status=\"@appliedFilter.Status\"" })
            Assert.Equal(3, CountOccurrences(view, route));
        // Enter trong ô mã gửi nút submit đầu tiên của form, phải là "Tìm kiếm".
        Assert.True(view.IndexOf(">Tìm kiếm</button>", StringComparison.Ordinal) < view.IndexOf(">Áp dụng bộ lọc</button>", StringComparison.Ordinal));
        Assert.Equal(1, CountOccurrences(view, "type=\"submit\" name=\"op\" value=\"@LoanLookupController.SearchOperation\""));
    }

    // ---------- Helpers ----------

    private LoanLookupController CreateController() => new(service, NullLogger<LoanLookupController>.Instance);

    private static IEnumerable<long> Ids(LoanLookupPage? page) => page!.Items.Select(item => item.LoanId);

    private static RedirectToActionResult Redirect(Task<IActionResult> action) =>
        Assert.IsType<RedirectToActionResult>(action.GetAwaiter().GetResult());

    private static void AssertRoute(RedirectToActionResult redirect, string q, string? from, string? to, string? status)
    {
        Assert.Equal(nameof(LoanLookupController.Index), redirect.ActionName);
        Assert.Equal(q, redirect.RouteValues!["q"]);
        Assert.Equal(from, redirect.RouteValues["from"]);
        Assert.Equal(to, redirect.RouteValues["to"]);
        Assert.Equal(status, redirect.RouteValues["status"]);
    }

    private static async Task<LoanLookupViewModel> ModelAsync(Task<IActionResult> action)
    {
        var view = Assert.IsType<ViewResult>(await action);
        return Assert.IsType<LoanLookupViewModel>(view.Model);
    }

    private async Task<Book> GetBookAsync()
    {
        if (book is not null) return book;
        book = new Book { Title = "Sách lọc", Author = new Author { Name = "Tác giả lọc" } };
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
            Email = $"filter{sequence}@example.com", PhoneNumber = "0900000000", StudentOrStaffCode = $"SV{sequence}",
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

    private async Task SetCardCodeAsync(ReaderAccount reader, string cardCode) =>
        await db.LibraryCards.Where(card => card.ReaderAccountId == reader.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(card => card.CardCode, cardCode));

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

    /// <summary>Thêm <paramref name="count"/> phiếu, mỗi ngày một phiếu bắt đầu từ <paramref name="start"/>.</summary>
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

    private sealed class CountingLookupService(ILoanLookupService inner) : ILoanLookupService
    {
        public int Calls { get; private set; }

        public Task<LoanLookupPage?> SearchAsync(string? code, int page, CancellationToken cancellationToken = default)
        {
            Calls++;
            return inner.SearchAsync(code, page, cancellationToken);
        }

        public Task<LoanLookupPage?> SearchAsync(string? code, int page, LoanLookupFilter filter, CancellationToken cancellationToken = default)
        {
            Calls++;
            return inner.SearchAsync(code, page, filter, cancellationToken);
        }
    }
}
