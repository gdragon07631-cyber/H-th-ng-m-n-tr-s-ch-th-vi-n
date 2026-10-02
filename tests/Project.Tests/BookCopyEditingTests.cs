using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Project.Controllers;
using Project.Data;
using Project.Filters;
using Project.Models;
using Project.Services;

namespace Project.Tests;

public sealed class BookCopyEditingTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly ApplicationDbContext db;
    private readonly BookCopyService service;
    private Warehouse warehouseA = null!;
    private Warehouse warehouseB = null!;
    private Shelf shelfA1 = null!;
    private Shelf shelfB1 = null!;
    private Book book = null!;

    public BookCopyEditingTests()
    {
        connection.Open();
        db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        service = new BookCopyService(db);
        SeedAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    // ---------- Sửa kho, kệ, tình trạng, ghi chú; không sửa mã vạch ----------

    [Fact]
    public async Task LibrarianMovesCopyToAnotherWarehouseAndShelfAndUpdatesConditionAndNote()
    {
        var copy = await AddCopyAsync("BC-001");

        var result = await service.UpdateAsync(copy.Id, Edit(copy, warehouseB.Id, shelfB1.Id, BookCopyCondition.MinorDamage, note: "Rách bìa sau"), "thuthu@tv.vn");

        Assert.True(result.IsSuccess);
        var saved = await ReloadAsync(copy.Id);
        Assert.Equal(shelfB1.Id, saved.ShelfId);
        Assert.Equal(BookCopyCondition.MinorDamage, saved.PhysicalCondition);
        Assert.Equal("Rách bìa sau", saved.Note);
        Assert.Equal("BC-001", saved.CopyCode);
    }

    [Fact]
    public async Task BarcodeCannotBeChangedEvenIfTheFormSendsAnotherOne()
    {
        var copy = await AddCopyAsync("BC-001");
        var controller = Controller();
        var model = Edit(copy, warehouseA.Id, shelfA1.Id);
        model.CopyCode = "HACKED-999";

        await controller.Edit(copy.Id, model);

        Assert.Equal("BC-001", (await ReloadAsync(copy.Id)).CopyCode);
    }

    [Fact]
    public async Task ShelfMustBelongToTheChosenWarehouse()
    {
        var copy = await AddCopyAsync("BC-001");

        var result = await service.UpdateAsync(copy.Id, Edit(copy, warehouseA.Id, shelfB1.Id), "thuthu@tv.vn");

        Assert.Equal(BookCopyUpdateStatus.InvalidShelf, result.Status);
        Assert.Equal(shelfA1.Id, (await ReloadAsync(copy.Id)).ShelfId);
    }

    [Fact]
    public async Task InactiveShelfIsRejected()
    {
        var copy = await AddCopyAsync("BC-001");
        shelfB1.Status = ShelfStatus.Inactive;
        await db.SaveChangesAsync();

        Assert.Equal(BookCopyUpdateStatus.InvalidShelf,
            (await service.UpdateAsync(copy.Id, Edit(copy, warehouseB.Id, shelfB1.Id), "thuthu@tv.vn")).Status);
    }

    [Fact]
    public async Task UnknownConditionIsRejected()
    {
        var copy = await AddCopyAsync("BC-001");

        Assert.Equal(BookCopyUpdateStatus.InvalidCondition,
            (await service.UpdateAsync(copy.Id, Edit(copy, warehouseA.Id, shelfA1.Id, "Như mới tinh"), "thuthu@tv.vn")).Status);
    }

    // ---------- Đang sửa chữa và số bản rảnh trên trang công khai ----------

    [Fact]
    public async Task CopyUnderRepairIsNotCountedAsAvailableOnPublicPage()
    {
        var first = await AddCopyAsync("BC-001");
        await AddCopyAsync("BC-002");
        var bookService = new BookService(db, NullLogger<BookService>.Instance);
        Assert.Equal(2, (await bookService.GetBookDetailsAsync(book.Id))!.AvailableCopies);

        await service.UpdateAsync(first.Id, Edit(first, warehouseA.Id, shelfA1.Id, status: BookCopyStatus.UnderRepair, reason: "Bong gáy"), "thuthu@tv.vn");

        var details = (await bookService.GetBookDetailsAsync(book.Id))!;
        Assert.Equal(1, details.AvailableCopies);
        Assert.Equal(2, details.TotalCopies);
    }

    [Fact]
    public async Task RepairedCopyCountsAsAvailableAgain()
    {
        var copy = await AddCopyAsync("BC-001", BookCopyStatus.UnderRepair);

        await service.UpdateAsync(copy.Id, Edit(copy, warehouseA.Id, shelfA1.Id, status: BookCopyStatus.Available, reason: "Đã đóng lại gáy"), "thuthu@tv.vn");

        Assert.Equal((1, 1), await service.CountCopiesAsync(book.Id));
    }

    // ---------- Không cho sửa chữa bản đang thuộc phiếu mượn chưa trả ----------

    [Fact]
    public async Task CopyOnAnOpenLoanCannotBeSentToRepair()
    {
        var copy = await AddCopyAsync("BC-001", BookCopyStatus.OnLoan);

        var result = await service.UpdateAsync(copy.Id, Edit(copy, warehouseA.Id, shelfA1.Id, status: BookCopyStatus.UnderRepair, reason: "Hỏng"), "thuthu@tv.vn");

        Assert.Equal(BookCopyUpdateStatus.OnActiveLoan, result.Status);
        Assert.Contains("phiếu mượn chưa trả", result.ErrorMessage);
        Assert.Equal(BookCopyStatus.OnLoan, (await ReloadAsync(copy.Id)).Status);
        Assert.Empty(await db.BookCopyStatusHistories.ToListAsync());
    }

    [Fact]
    public async Task CopyOnLoanCanStillHaveItsNoteUpdatedWithoutChangingStatus()
    {
        var copy = await AddCopyAsync("BC-001", BookCopyStatus.OnLoan);

        var result = await service.UpdateAsync(copy.Id, Edit(copy, warehouseA.Id, shelfA1.Id, note: "Bạn đọc báo bị ướt", status: BookCopyStatus.OnLoan), "thuthu@tv.vn");

        Assert.True(result.IsSuccess);
        Assert.Equal("Bạn đọc báo bị ướt", (await ReloadAsync(copy.Id)).Note);
    }

    [Fact]
    public async Task CopyHeldForAReservationCannotChangeStatus()
    {
        var copy = await AddCopyAsync("BC-001", BookCopyStatus.OnHold);

        var result = await service.UpdateAsync(copy.Id, Edit(copy, warehouseA.Id, shelfA1.Id, status: BookCopyStatus.UnderRepair, reason: "Hỏng"), "thuthu@tv.vn");

        Assert.Equal(BookCopyUpdateStatus.StatusManagedByHold, result.Status);
    }

    [Theory]
    [InlineData(BookCopyStatus.OnLoan)]
    [InlineData(BookCopyStatus.OnHold)]
    [InlineData("Mất")]
    public async Task LibrarianCanOnlyChooseAvailableOrUnderRepair(string target)
    {
        var copy = await AddCopyAsync("BC-001");

        var result = await service.UpdateAsync(copy.Id, Edit(copy, warehouseA.Id, shelfA1.Id, status: target, reason: "Thử"), "thuthu@tv.vn");

        Assert.Equal(BookCopyUpdateStatus.InvalidStatus, result.Status);
        Assert.Equal(BookCopyStatus.Available, (await ReloadAsync(copy.Id)).Status);
    }

    // ---------- Ghi lại người thực hiện, thời điểm và lý do ----------

    [Fact]
    public async Task EveryStatusChangeIsRecordedWithActorTimeAndReason()
    {
        var copy = await AddCopyAsync("BC-001");

        await service.UpdateAsync(copy.Id, Edit(copy, warehouseA.Id, shelfA1.Id, status: BookCopyStatus.UnderRepair, reason: "Bong gáy"), "thuthu@tv.vn");
        await service.UpdateAsync(copy.Id, Edit(copy, warehouseA.Id, shelfA1.Id, status: BookCopyStatus.Available, reason: "Đã sửa xong"), "quanly@tv.vn");

        var history = await service.GetHistoryAsync(copy.Id);
        Assert.Equal(2, history.Count);
        Assert.Equal((BookCopyStatus.UnderRepair, BookCopyStatus.Available, "Đã sửa xong", "quanly@tv.vn"),
            (history[0].FromStatus, history[0].ToStatus, history[0].Reason, history[0].ChangedBy));
        Assert.Equal((BookCopyStatus.Available, BookCopyStatus.UnderRepair, "Bong gáy", "thuthu@tv.vn"),
            (history[1].FromStatus, history[1].ToStatus, history[1].Reason, history[1].ChangedBy));
        Assert.All(history, item => Assert.InRange(item.ChangedAtUtc, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1)));
    }

    [Fact]
    public async Task ChangingStatusRequiresAReason()
    {
        var copy = await AddCopyAsync("BC-001");

        var result = await service.UpdateAsync(copy.Id, Edit(copy, warehouseA.Id, shelfA1.Id, status: BookCopyStatus.UnderRepair, reason: "  "), "thuthu@tv.vn");

        Assert.Equal(BookCopyUpdateStatus.ReasonRequired, result.Status);
        Assert.Equal(BookCopyStatus.Available, (await ReloadAsync(copy.Id)).Status);
    }

    [Fact]
    public async Task EditingWithoutStatusChangeWritesNoHistory()
    {
        var copy = await AddCopyAsync("BC-001");

        await service.UpdateAsync(copy.Id, Edit(copy, warehouseB.Id, shelfB1.Id), "thuthu@tv.vn");

        Assert.Empty(await service.GetHistoryAsync(copy.Id));
    }

    [Fact]
    public async Task ScreenRecordsTheSignedInStaffAsActor()
    {
        var copy = await AddCopyAsync("BC-001");
        var account = new AdminAccount { Email = "lan.thuthu@tv.vn", Role = AccountRoles.Librarian, IsActive = true, PasswordHash = "x" };
        var token = Guid.NewGuid().ToString("N");
        db.RefreshTokens.Add(new RefreshToken { AdminAccount = account, TokenHash = TokenService.HashRefreshToken(token), CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(1) });
        await db.SaveChangesAsync();

        var redirect = await Controller($"admin_refresh={token}")
            .Edit(copy.Id, Edit(copy, warehouseA.Id, shelfA1.Id, status: BookCopyStatus.UnderRepair, reason: "Bong gáy"));

        Assert.IsType<RedirectToActionResult>(redirect);
        Assert.Equal("lan.thuthu@tv.vn", (await service.GetHistoryAsync(copy.Id)).Single().ChangedBy);
    }

    // ---------- Thêm bản sao & phân quyền ----------

    [Fact]
    public async Task AddingCopyRejectsDuplicateBarcode()
    {
        await AddCopyAsync("BC-001");

        var result = await service.AddAsync(book.Id, new NewBookCopyViewModel { CopyCode = "BC-001", ShelfId = shelfA1.Id });

        Assert.Equal(BookCopyUpdateStatus.DuplicateCode, result.Status);
        Assert.Contains("BC-001", result.ErrorMessage);
    }

    [Fact]
    public async Task AddedCopyStartsAvailableOnTheChosenShelf()
    {
        var result = await service.AddAsync(book.Id, new NewBookCopyViewModel { CopyCode = " BC-009 ", ShelfId = shelfB1.Id, PhysicalCondition = BookCopyCondition.Worn });

        Assert.True(result.IsSuccess);
        var saved = await ReloadAsync(result.Copy!.Id);
        Assert.Equal(("BC-009", BookCopyStatus.Available, shelfB1.Id, BookCopyCondition.Worn), (saved.CopyCode, saved.Status, saved.ShelfId, saved.PhysicalCondition));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(50)]
    public async Task BatchCreatesRequestedCopiesWithConsecutiveBarcodesAndReceivedDate(int quantity)
    {
        var receivedDate = new DateOnly(2026, 10, 2);

        var result = await service.AddBatchAsync(book.Id, new NewBookCopyBatchViewModel
        {
            Quantity = quantity,
            WarehouseId = warehouseA.Id,
            ShelfId = shelfA1.Id,
            ReceivedDate = receivedDate
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(quantity, result.Copies!.Count);
        Assert.Empty(result.SkippedBarcodes!);
        Assert.Equal(Enumerable.Range(1, quantity).Select(number => number.ToString("D6")), result.Copies.Select(copy => copy.CopyCode));
        Assert.All(result.Copies, copy =>
        {
            Assert.Equal(receivedDate, copy.ReceivedDate);
            Assert.Equal(shelfA1.Id, copy.ShelfId);
            Assert.Equal(warehouseA.Name, copy.Shelf?.Warehouse?.Name);
        });
        Assert.Equal(quantity, await db.BookCopies.CountAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task BatchRejectsQuantityOutsideOneThroughFifty(int quantity)
    {
        var result = await service.AddBatchAsync(book.Id, new NewBookCopyBatchViewModel
        {
            Quantity = quantity,
            WarehouseId = warehouseA.Id,
            ShelfId = shelfA1.Id,
            ReceivedDate = new DateOnly(2026, 10, 2)
        });

        Assert.Equal(BookCopyBatchCreateStatus.InvalidQuantity, result.Status);
        Assert.Empty(await db.BookCopies.ToListAsync());
    }

    [Theory]
    [InlineData(1, "000001", "000001")]
    [InlineData(5, "000001", "000005")]
    [InlineData(10, "000001", "000010")]
    [InlineData(50, "000001", "000050")]
    public async Task PreviewReturnsTheAc1BarcodeRangeWithoutSaving(int quantity, string expectedStart, string expectedEnd)
    {
        var result = await service.PreviewBatchAsync(quantity);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedStart, result.StartBarcode);
        Assert.Equal(expectedEnd, result.EndBarcode);
        Assert.Equal($"{expectedStart} - {expectedEnd}", result.DisplayRange);
        Assert.Empty(await db.BookCopies.ToListAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task PreviewRejectsInvalidQuantityWithoutReturningARange(int quantity)
    {
        var result = await service.PreviewBatchAsync(quantity);

        Assert.False(result.IsSuccess);
        Assert.Null(result.DisplayRange);
        Assert.Contains("1 đến 50", result.ErrorMessage);
        Assert.Empty(await db.BookCopies.ToListAsync());
    }

    [Fact]
    public async Task PreviewStartsAfterTheLastBarcodeGeneratedByAc1()
    {
        await service.AddBatchAsync(book.Id, new NewBookCopyBatchViewModel
        {
            Quantity = 5,
            WarehouseId = warehouseA.Id,
            ShelfId = shelfA1.Id,
            ReceivedDate = new DateOnly(2026, 10, 2)
        });

        var preview = await service.PreviewBatchAsync(3);

        Assert.Equal("000006", preview.StartBarcode);
        Assert.Equal("000008", preview.EndBarcode);
        Assert.Equal(5, await db.BookCopies.CountAsync());
    }

    [Fact]
    public async Task LabelPreviewUsesOnlyRequestedSavedCopiesInOrderWithPngBarcodes()
    {
        var batch = await service.AddBatchAsync(book.Id, new NewBookCopyBatchViewModel
        {
            Quantity = 3,
            WarehouseId = warehouseA.Id,
            ShelfId = shelfA1.Id,
            ReceivedDate = new DateOnly(2026, 10, 2)
        });
        var ids = batch.Copies!.Select(copy => copy.Id).ToArray();

        var labels = await service.GetLabelsForCopiesAsync(book.Id, [ids[2], ids[0]]);

        Assert.NotNull(labels);
        Assert.Equal(new[] { "000003", "000001" }, labels.Labels.Select(label => label.CopyCode));
        Assert.Equal(new[] { book.Title, book.Title }, labels.Labels.Select(label => label.BookTitle));
        Assert.Equal(new[] { warehouseA.Code, warehouseA.Code }, labels.Labels.Select(label => label.WarehouseCode));
        Assert.All(labels.Labels, label =>
        {
            Assert.StartsWith("data:image/png;base64,", label.BarcodeImageDataUri);
            var png = Convert.FromBase64String(label.BarcodeImageDataUri["data:image/png;base64,".Length..]);
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        });
        Assert.Equal(3, await db.BookCopies.CountAsync());
    }

    [Fact]
    public async Task LabelPreviewRejectsCopyIdsFromAnotherBook()
    {
        var copy = await AddCopyAsync("BC-001");
        var otherBook = new Book { Title = "Khác", AuthorId = book.AuthorId };
        db.Books.Add(otherBook);
        await db.SaveChangesAsync();

        var labels = await service.GetLabelsForCopiesAsync(otherBook.Id, [copy.Id]);

        Assert.Null(labels);
    }

    [Fact]
    public async Task BatchScreenShowsTheCopiesJustSaved()
    {
        var result = await Controller().CreateBatch(book.Id, new NewBookCopyBatchViewModel
        {
            Quantity = 5,
            WarehouseId = warehouseA.Id,
            ShelfId = shelfA1.Id,
            ReceivedDate = new DateOnly(2026, 10, 2)
        });

        var view = Assert.IsType<ViewResult>(result);
        var page = Assert.IsType<BookCopyIndexViewModel>(view.Model);
        Assert.Equal(5, page.CreatedCopies.Count);
        Assert.Equal("000001", page.CreatedCopies[0].CopyCode);
        Assert.Equal("000005", page.CreatedCopies[^1].CopyCode);
        Assert.Empty(page.SkippedBarcodes);
        Assert.Equal(5, await db.BookCopies.CountAsync());
    }

    [Fact]
    public void CopyScreensAreForLibraryStaffOnly()
    {
        var attribute = typeof(BookCopyController).GetCustomAttribute<StaffOnlyAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(new[] { AccountRoles.Librarian, AccountRoles.LibraryManager, AccountRoles.SystemAdmin }.Order(), attribute.Roles.Order());
    }

    private async Task SeedAsync()
    {
        warehouseA = new Warehouse { Code = "KHO-A", Name = "Kho A" };
        warehouseB = new Warehouse { Code = "KHO-B", Name = "Kho B" };
        db.Warehouses.AddRange(warehouseA, warehouseB);
        await db.SaveChangesAsync();
        shelfA1 = new Shelf { Code = "A1", Name = "Kệ A1", WarehouseId = warehouseA.Id };
        shelfB1 = new Shelf { Code = "B1", Name = "Kệ B1", WarehouseId = warehouseB.Id };
        db.Shelves.AddRange(shelfA1, shelfB1);
        var author = new Author { Name = "Nam Cao" };
        db.Authors.Add(author);
        await db.SaveChangesAsync();
        book = new Book { Title = "Chí Phèo", AuthorId = author.Id };
        db.Books.Add(book);
        await db.SaveChangesAsync();
    }

    private async Task<BookCopy> AddCopyAsync(string code, string status = BookCopyStatus.Available)
    {
        var copy = new BookCopy { BookId = book.Id, ShelfId = shelfA1.Id, CopyCode = code, Status = status };
        db.BookCopies.Add(copy);
        await db.SaveChangesAsync();
        return copy;
    }

    private static BookCopyEditViewModel Edit(BookCopy copy, int warehouseId, int shelfId, string condition = BookCopyCondition.Good,
        string? note = null, string? status = null, string? reason = null) => new()
    {
        Id = copy.Id,
        WarehouseId = warehouseId,
        ShelfId = shelfId,
        PhysicalCondition = condition,
        Note = note,
        Status = status ?? copy.Status,
        Reason = reason
    };

    private Task<BookCopy> ReloadAsync(long id) => db.BookCopies.AsNoTracking().SingleAsync(copy => copy.Id == id);

    private BookCopyController Controller(string? cookie = null)
    {
        var context = new DefaultHttpContext();
        if (cookie is not null) context.Request.Headers.Cookie = cookie;
        return new BookCopyController(service, new AuditLogService(db, NullLogger<AuditLogService>.Instance))
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            TempData = new TempDataDictionary(context, new NoTempDataProvider())
        };
    }

    private sealed class NoTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
