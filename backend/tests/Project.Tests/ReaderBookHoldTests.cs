using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Net.Http.Headers;
using Project.Controllers;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Tests;

/// <summary>S2-08: Bạn đọc xem và tự hủy các đơn đặt giữ của mình (Lát 1 + Lát 2).</summary>
public sealed class ReaderBookHoldTests : IDisposable
{
    // SQLite thay vì InMemory: việc hủy đơn chạy trong giao dịch (Lát 3), cần provider quan hệ thật.
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly ApplicationDbContext db;
    private readonly IDataProtectionProvider dataProtection = new EphemeralDataProtectionProvider();
    private readonly ReaderRegistrationService service;

    public ReaderBookHoldTests()
    {
        connection.Open();
        db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        service = new ReaderRegistrationService(db, new PasswordHasher<ReaderAccount>(),
            NullLogger<ReaderRegistrationService>.Instance);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    [Fact]
    public async Task ReaderSeesOnlyOwnHoldsWithTitleDateAndStatus()
    {
        var (owner, other) = await SeedReadersAsync();
        var bookA = await AddBookAsync("Sách A");
        var bookB = await AddBookAsync("Sách B");
        var heldAt = new DateTime(2026, 9, 30, 8, 0, 0, DateTimeKind.Utc);
        await AddHoldAsync(owner, bookA, BookHoldStatus.Waiting, heldAt);
        await AddHoldAsync(owner, bookB, BookHoldStatus.Available, heldAt.AddDays(-1));
        await AddHoldAsync(other, bookA, BookHoldStatus.Waiting, heldAt);

        var holds = await service.GetReaderHoldsAsync(owner.Id);

        Assert.Equal(2, holds.Count);
        Assert.Equal(["Sách A", "Sách B"], holds.Select(hold => hold.BookTitle));
        Assert.Equal(heldAt, holds[0].HeldAtUtc);
        Assert.Equal(BookHoldStatus.Waiting, holds[0].Status);
        Assert.True(holds[0].CanCancel);
        Assert.Equal(BookHoldStatus.Available, holds[1].Status);
        Assert.False(holds[1].CanCancel);
    }

    [Fact]
    public async Task CancellingWaitingHoldMarksItCancelled()
    {
        var (owner, _) = await SeedReadersAsync();
        var hold = await AddHoldAsync(owner, await AddBookAsync("Sách A"), BookHoldStatus.Waiting);

        var outcome = await service.CancelReaderHoldAsync(owner.Id, hold.Id);

        Assert.Equal(BookHoldCancelResult.Success, outcome.Result);
        Assert.Equal(BookHoldStatus.Cancelled, outcome.Status);
        Assert.Equal(BookHoldStatus.Cancelled, (await ReloadAsync(hold.Id)).Status);
        Assert.False((await service.GetReaderHoldsAsync(owner.Id)).Single().CanCancel);
    }

    [Theory]
    [InlineData(BookHoldStatus.Available)]
    [InlineData(BookHoldStatus.Cancelled)]
    [InlineData(BookHoldStatus.ConvertedToLoan)]
    public async Task HoldsNotWaitingCannotBeCancelled(string status)
    {
        var (owner, _) = await SeedReadersAsync();
        var hold = await AddHoldAsync(owner, await AddBookAsync("Sách A"), status);

        var outcome = await service.CancelReaderHoldAsync(owner.Id, hold.Id);

        Assert.Equal(BookHoldCancelResult.NotWaiting, outcome.Result);
        Assert.Contains(status, outcome.Message);
        Assert.Equal(status, (await ReloadAsync(hold.Id)).Status);
    }

    [Fact]
    public async Task ReaderCannotCancelAnotherReadersHold()
    {
        var (owner, other) = await SeedReadersAsync();
        var hold = await AddHoldAsync(other, await AddBookAsync("Sách A"), BookHoldStatus.Waiting);

        var outcome = await service.CancelReaderHoldAsync(owner.Id, hold.Id);

        Assert.Equal(BookHoldCancelResult.NotFound, outcome.Result);
        Assert.Equal(BookHoldStatus.Waiting, (await ReloadAsync(hold.Id)).Status);
    }

    [Fact]
    public async Task HoldsApiReturnsOnlySignedInReadersHolds()
    {
        var (owner, other) = await SeedReadersAsync();
        var book = await AddBookAsync("Sách A");
        var ownHold = await AddHoldAsync(owner, book, BookHoldStatus.Waiting);
        await AddHoldAsync(other, book, BookHoldStatus.Waiting);

        var result = Assert.IsType<OkObjectResult>(await CreateController(SignIn(owner)).GetMyHoldsApi());

        var item = Assert.Single(Assert.IsAssignableFrom<IEnumerable<object>>(result.Value));
        Assert.Equal(ownHold.Id, (long)item.GetType().GetProperty("id")!.GetValue(item)!);
        Assert.Equal("Sách A", item.GetType().GetProperty("bookTitle")!.GetValue(item));
    }

    [Fact]
    public async Task HoldsApiAndPageRequireSignIn()
    {
        var controller = CreateController(null);

        Assert.IsType<UnauthorizedObjectResult>(await controller.GetMyHoldsApi());
        Assert.IsType<UnauthorizedObjectResult>(await controller.CancelMyHoldApi(1));
        var redirect = Assert.IsType<RedirectToActionResult>(await controller.MyHolds());
        Assert.Equal(nameof(ReaderRegistrationController.Login), redirect.ActionName);
    }

    [Fact]
    public async Task CancelApiCancelsOwnWaitingHold()
    {
        var (owner, _) = await SeedReadersAsync();
        var hold = await AddHoldAsync(owner, await AddBookAsync("Sách A"), BookHoldStatus.Waiting);

        Assert.IsType<OkObjectResult>(await CreateController(SignIn(owner)).CancelMyHoldApi(hold.Id));
        Assert.Equal(BookHoldStatus.Cancelled, (await ReloadAsync(hold.Id)).Status);
    }

    [Fact]
    public async Task CancelApiRejectsAnotherReadersHold()
    {
        var (owner, other) = await SeedReadersAsync();
        var hold = await AddHoldAsync(other, await AddBookAsync("Sách A"), BookHoldStatus.Waiting);

        Assert.IsType<NotFoundObjectResult>(await CreateController(SignIn(owner)).CancelMyHoldApi(hold.Id));
        Assert.Equal(BookHoldStatus.Waiting, (await ReloadAsync(hold.Id)).Status);
    }

    [Fact]
    public async Task CancelApiReturnsConflictForHoldThatIsNotWaiting()
    {
        var (owner, _) = await SeedReadersAsync();
        var hold = await AddHoldAsync(owner, await AddBookAsync("Sách A"), BookHoldStatus.ConvertedToLoan);

        Assert.IsType<ConflictObjectResult>(await CreateController(SignIn(owner)).CancelMyHoldApi(hold.Id));
        Assert.Equal(BookHoldStatus.ConvertedToLoan, (await ReloadAsync(hold.Id)).Status);
    }

    // ---- Lát 2: vị trí hàng đợi, hạn nhận sách, chặn hủy đơn đã chuyển thành phiếu mượn ----

    [Fact]
    public async Task WaitingHoldShowsQueuePositionAmongWaitingHoldsOfSameBook()
    {
        var (owner, other) = await SeedReadersAsync();
        var third = await AddReaderAsync("third@example.com", "R-3");
        var fourth = await AddReaderAsync("fourth@example.com", "R-4");
        var book = await AddBookAsync("Sách A");
        var otherBook = await AddBookAsync("Sách B");
        var start = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);
        // Đơn đã có sách / đã hủy đặt sớm hơn không còn nằm trong hàng đợi.
        await AddHoldAsync(third, book, BookHoldStatus.Available, start.AddDays(-2));
        await AddHoldAsync(fourth, book, BookHoldStatus.Cancelled, start.AddDays(-1));
        await AddHoldAsync(other, book, BookHoldStatus.Waiting, start);
        var ownHold = await AddHoldAsync(owner, book, BookHoldStatus.Waiting, start.AddHours(1));
        await AddHoldAsync(other, otherBook, BookHoldStatus.Waiting, start.AddDays(-5));

        var item = (await service.GetReaderHoldsAsync(owner.Id)).Single(hold => hold.Id == ownHold.Id);

        Assert.Equal(2, item.QueuePosition);
        Assert.True(item.CanCancel);
    }

    [Fact]
    public async Task QueuePositionFollowsHoldOrderAndBreaksTiesById()
    {
        var (first, second) = await SeedReadersAsync();
        var third = await AddReaderAsync("third@example.com", "R-3");
        var book = await AddBookAsync("Sách A");
        var heldAt = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);
        await AddHoldAsync(third, book, BookHoldStatus.Waiting, heldAt.AddMinutes(5));
        await AddHoldAsync(first, book, BookHoldStatus.Waiting, heldAt);
        await AddHoldAsync(second, book, BookHoldStatus.Waiting, heldAt);

        Assert.Equal(1, (await service.GetReaderHoldsAsync(first.Id)).Single().QueuePosition);
        Assert.Equal(2, (await service.GetReaderHoldsAsync(second.Id)).Single().QueuePosition);
        Assert.Equal(3, (await service.GetReaderHoldsAsync(third.Id)).Single().QueuePosition);
    }

    [Fact]
    public async Task AvailableHoldShowsPickupDeadlineWithDateAndTime()
    {
        var (owner, _) = await SeedReadersAsync();
        var deadlineUtc = new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc);
        var hold = await AddHoldAsync(owner, await AddBookAsync("Sách A"), BookHoldStatus.Available,
            pickupDeadlineUtc: deadlineUtc);

        var item = (await service.GetReaderHoldsAsync(owner.Id)).Single();

        var expected = deadlineUtc.ToLocalTime().ToString("HH:mm dd/MM/yyyy");
        Assert.Equal(deadlineUtc, item.PickupDeadlineUtc);
        Assert.Equal(expected, item.PickupDeadlineText);
        Assert.Matches(@"^\d{2}:\d{2} \d{2}/\d{2}/\d{4}$", item.PickupDeadlineText!);
        Assert.Null(item.QueuePosition);
        Assert.False(item.CanCancel);

        var api = Assert.IsType<OkObjectResult>(await CreateController(SignIn(owner)).GetMyHoldsApi());
        var apiItem = Assert.Single(Assert.IsAssignableFrom<IEnumerable<object>>(api.Value));
        Assert.Equal(expected, apiItem.GetType().GetProperty("pickupDeadline")!.GetValue(apiItem));
        Assert.Equal(hold.Id, (long)apiItem.GetType().GetProperty("id")!.GetValue(apiItem)!);
    }

    [Theory]
    [InlineData(BookHoldStatus.Available, false)]
    [InlineData(BookHoldStatus.Waiting, true)]
    [InlineData(BookHoldStatus.Cancelled, true)]
    [InlineData(BookHoldStatus.ConvertedToLoan, true)]
    public async Task PickupDeadlineIsOnlyShownForAvailableHoldsThatHaveOne(string status, bool hasStaleDeadline)
    {
        var (owner, _) = await SeedReadersAsync();
        await AddHoldAsync(owner, await AddBookAsync("Sách A"), status,
            pickupDeadlineUtc: hasStaleDeadline ? DateTime.UtcNow.AddDays(1) : null);

        var item = (await service.GetReaderHoldsAsync(owner.Id)).Single();

        Assert.Null(item.PickupDeadlineUtc);
        Assert.Null(item.PickupDeadlineText);
    }

    [Fact]
    public async Task ConvertedToLoanHoldHasNoCancelActionQueueOrDeadline()
    {
        var (owner, _) = await SeedReadersAsync();
        await AddHoldAsync(owner, await AddBookAsync("Sách A"), BookHoldStatus.ConvertedToLoan);

        var item = (await service.GetReaderHoldsAsync(owner.Id)).Single();
        Assert.Equal(BookHoldStatus.ConvertedToLoan, item.Status);
        Assert.False(item.CanCancel);
        Assert.Null(item.QueuePosition);

        var api = Assert.IsType<OkObjectResult>(await CreateController(SignIn(owner)).GetMyHoldsApi());
        var apiItem = Assert.Single(Assert.IsAssignableFrom<IEnumerable<object>>(api.Value));
        Assert.Equal(false, apiItem.GetType().GetProperty("canCancel")!.GetValue(apiItem));
        Assert.Equal(BookHoldStatus.ConvertedToLoan, apiItem.GetType().GetProperty("status")!.GetValue(apiItem));
    }

    [Fact]
    public async Task CancelApiBlocksConvertedToLoanHoldWithClearMessage()
    {
        var (owner, _) = await SeedReadersAsync();
        var hold = await AddHoldAsync(owner, await AddBookAsync("Sách A"), BookHoldStatus.ConvertedToLoan);

        var result = Assert.IsType<ConflictObjectResult>(await CreateController(SignIn(owner)).CancelMyHoldApi(hold.Id));

        var message = (string)result.Value!.GetType().GetProperty("message")!.GetValue(result.Value)!;
        Assert.Contains("Không thể hủy", message);
        Assert.Contains(BookHoldStatus.ConvertedToLoan, message);
        Assert.Equal(BookHoldStatus.ConvertedToLoan, (await ReloadAsync(hold.Id)).Status);
    }

    [Fact]
    public async Task CancellingWaitingHoldRemovesItFromQueue()
    {
        var (owner, other) = await SeedReadersAsync();
        var book = await AddBookAsync("Sách A");
        var heldAt = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);
        // Người hủy không đứng đầu, để không kích hoạt việc đôn lượt (Lát 3).
        var head = await AddReaderAsync("head@example.com", "R-0");
        await AddHoldAsync(head, book, BookHoldStatus.Waiting, heldAt.AddHours(-1));
        var ownHold = await AddHoldAsync(owner, book, BookHoldStatus.Waiting, heldAt);
        await AddHoldAsync(other, book, BookHoldStatus.Waiting, heldAt.AddHours(1));

        Assert.IsType<OkObjectResult>(await CreateController(SignIn(owner)).CancelMyHoldApi(ownHold.Id));

        var ownItem = (await service.GetReaderHoldsAsync(owner.Id)).Single();
        Assert.Equal(BookHoldStatus.Cancelled, ownItem.Status);
        Assert.Null(ownItem.QueuePosition);
        Assert.Equal(2, (await service.GetReaderHoldsAsync(other.Id)).Single().QueuePosition);
    }

    // ---- Lát 3: đôn lượt người tiếp theo và cập nhật bản sao khi hủy đơn ----

    [Fact]
    public async Task SoleWaitingReaderCancelsAndHeldCopyBecomesAvailable()
    {
        var (owner, _) = await SeedReadersAsync();
        var book = await AddBookAsync("Sách A");
        var copy = await AddCopyAsync(book, "A-1", BookCopyStatus.OnHold);
        var hold = await AddHoldAsync(owner, book, BookHoldStatus.Waiting, copy: copy);

        var outcome = await service.CancelReaderHoldAsync(owner.Id, hold.Id);

        Assert.Equal(BookHoldCancelResult.Success, outcome.Result);
        Assert.Null(outcome.PromotedHoldId);
        Assert.Equal(copy.Id, outcome.ReleasedCopyId);
        var cancelled = await ReloadAsync(hold.Id);
        Assert.Equal(BookHoldStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.BookCopyId);
        Assert.Equal(BookCopyStatus.Available, (await ReloadCopyAsync(copy.Id)).Status);
    }

    [Fact]
    public async Task CancellingFirstInQueuePromotesNextReaderWithPickupDeadline()
    {
        var (first, second) = await SeedReadersAsync();
        var third = await AddReaderAsync("third@example.com", "R-3");
        var book = await AddBookAsync("Sách A");
        var copy = await AddCopyAsync(book, "A-1", BookCopyStatus.OnHold);
        var heldAt = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);
        var firstHold = await AddHoldAsync(first, book, BookHoldStatus.Waiting, heldAt, copy: copy);
        var secondHold = await AddHoldAsync(second, book, BookHoldStatus.Waiting, heldAt.AddHours(1));
        await AddHoldAsync(third, book, BookHoldStatus.Waiting, heldAt.AddHours(2));

        var result = Assert.IsType<OkObjectResult>(await CreateController(SignIn(first)).CancelMyHoldApi(firstHold.Id));

        Assert.Equal(secondHold.Id, (long?)result.Value!.GetType().GetProperty("promotedHoldId")!.GetValue(result.Value));
        Assert.Equal(BookHoldStatus.Cancelled, (await ReloadAsync(firstHold.Id)).Status);
        var promoted = await ReloadAsync(secondHold.Id);
        Assert.Equal(BookHoldStatus.Available, promoted.Status);
        Assert.Equal(await ExpectedPickupDeadlineUtcAsync(), promoted.PickupDeadlineUtc);
        Assert.Equal(copy.Id, promoted.BookCopyId);
        // Bản sao chuyển cho người được đôn, không trả về "Sẵn sàng".
        Assert.Equal(BookCopyStatus.OnHold, (await ReloadCopyAsync(copy.Id)).Status);

        var secondItem = (await service.GetReaderHoldsAsync(second.Id)).Single();
        Assert.Equal(BookHoldStatus.Available, secondItem.Status);
        Assert.StartsWith("17:00 ", secondItem.PickupDeadlineText);
        Assert.Equal(1, (await service.GetReaderHoldsAsync(third.Id)).Single().QueuePosition);
    }

    [Fact]
    public async Task PromotionWithoutLinkedCopyStillGivesNextReaderAPickupDeadline()
    {
        var (first, second) = await SeedReadersAsync();
        var book = await AddBookAsync("Sách A");
        var heldAt = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);
        var firstHold = await AddHoldAsync(first, book, BookHoldStatus.Waiting, heldAt);
        var secondHold = await AddHoldAsync(second, book, BookHoldStatus.Waiting, heldAt.AddHours(1));

        var outcome = await service.CancelReaderHoldAsync(first.Id, firstHold.Id);

        Assert.Equal(secondHold.Id, outcome.PromotedHoldId);
        Assert.Null(outcome.ReleasedCopyId);
        var promoted = await ReloadAsync(secondHold.Id);
        Assert.Equal(BookHoldStatus.Available, promoted.Status);
        Assert.NotNull(promoted.PickupDeadlineUtc);
        Assert.Null(promoted.BookCopyId);
    }

    [Fact]
    public async Task CancellingMiddleOfQueueMovesLaterReadersUpByOneWithoutPromotion()
    {
        var readers = new List<ReaderAccount>();
        for (var i = 1; i <= 4; i++) readers.Add(await AddReaderAsync($"q{i}@example.com", $"Q-{i}"));
        var book = await AddBookAsync("Sách A");
        var heldAt = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);
        var holds = new List<BookHold>();
        for (var i = 0; i < 4; i++)
            holds.Add(await AddHoldAsync(readers[i], book, BookHoldStatus.Waiting, heldAt.AddHours(i)));
        var copy = await AddCopyAsync(book, "A-1", BookCopyStatus.OnHold);
        var servedReader = await AddReaderAsync("served@example.com", "S-1");
        var served = await AddHoldAsync(servedReader, book, BookHoldStatus.Available, heldAt.AddDays(-1),
            pickupDeadlineUtc: heldAt.AddDays(1), copy: copy);
        var loanReader = await AddReaderAsync("loan@example.com", "L-1");
        var loaned = await AddHoldAsync(loanReader, book, BookHoldStatus.ConvertedToLoan, heldAt.AddDays(-2));

        var outcome = await service.CancelReaderHoldAsync(readers[1].Id, holds[1].Id);

        Assert.Null(outcome.PromotedHoldId);
        Assert.Equal(BookHoldStatus.Cancelled, (await ReloadAsync(holds[1].Id)).Status);
        Assert.Equal(1, await PositionAsync(readers[0]));
        Assert.Equal(2, await PositionAsync(readers[2]));
        Assert.Equal(3, await PositionAsync(readers[3]));
        foreach (var index in new[] { 0, 2, 3 })
            Assert.Equal(BookHoldStatus.Waiting, (await ReloadAsync(holds[index].Id)).Status);
        var servedAfter = await ReloadAsync(served.Id);
        Assert.Equal(BookHoldStatus.Available, servedAfter.Status);
        Assert.Equal(heldAt.AddDays(1), servedAfter.PickupDeadlineUtc);
        Assert.Equal(copy.Id, servedAfter.BookCopyId);
        Assert.Equal(BookHoldStatus.ConvertedToLoan, (await ReloadAsync(loaned.Id)).Status);
        Assert.Equal(BookCopyStatus.OnHold, (await ReloadCopyAsync(copy.Id)).Status);
    }

    [Fact]
    public async Task CancellingConvertedToLoanHoldChangesNeitherQueueNorCopy()
    {
        var (owner, other) = await SeedReadersAsync();
        var book = await AddBookAsync("Sách A");
        var copy = await AddCopyAsync(book, "A-1", BookCopyStatus.OnLoan);
        var heldAt = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);
        var loaned = await AddHoldAsync(owner, book, BookHoldStatus.ConvertedToLoan, heldAt, copy: copy);
        var waiting = await AddHoldAsync(other, book, BookHoldStatus.Waiting, heldAt.AddHours(1));

        var result = Assert.IsType<ConflictObjectResult>(await CreateController(SignIn(owner)).CancelMyHoldApi(loaned.Id));

        Assert.Contains(BookHoldStatus.ConvertedToLoan,
            (string)result.Value!.GetType().GetProperty("message")!.GetValue(result.Value)!);
        var loanedAfter = await ReloadAsync(loaned.Id);
        Assert.Equal(BookHoldStatus.ConvertedToLoan, loanedAfter.Status);
        Assert.Equal(copy.Id, loanedAfter.BookCopyId);
        var waitingAfter = await ReloadAsync(waiting.Id);
        Assert.Equal(BookHoldStatus.Waiting, waitingAfter.Status);
        Assert.Null(waitingAfter.PickupDeadlineUtc);
        Assert.Equal(1, await PositionAsync(other));
        Assert.Equal(BookCopyStatus.OnLoan, (await ReloadCopyAsync(copy.Id)).Status);
    }

    [Fact]
    public async Task CancellingHoldOfOneBookDoesNotTouchAnotherBooksQueueOrCopies()
    {
        var (owner, other) = await SeedReadersAsync();
        var third = await AddReaderAsync("third@example.com", "R-3");
        var bookA = await AddBookAsync("Sách A");
        var bookB = await AddBookAsync("Sách B");
        var copyA = await AddCopyAsync(bookA, "A-1", BookCopyStatus.OnHold);
        var copyB = await AddCopyAsync(bookB, "B-1", BookCopyStatus.OnHold);
        var heldAt = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);
        var ownHold = await AddHoldAsync(owner, bookA, BookHoldStatus.Waiting, heldAt, copy: copyA);
        // Sách B có người chờ sớm hơn: không được bị đôn khi hủy đơn sách A.
        var holdB1 = await AddHoldAsync(other, bookB, BookHoldStatus.Waiting, heldAt.AddDays(-1), copy: copyB);
        var holdB2 = await AddHoldAsync(third, bookB, BookHoldStatus.Waiting, heldAt.AddHours(1));

        var outcome = await service.CancelReaderHoldAsync(owner.Id, ownHold.Id);

        Assert.Null(outcome.PromotedHoldId);
        Assert.Equal(BookCopyStatus.Available, (await ReloadCopyAsync(copyA.Id)).Status);
        Assert.Equal(BookCopyStatus.OnHold, (await ReloadCopyAsync(copyB.Id)).Status);
        var b1 = await ReloadAsync(holdB1.Id);
        Assert.Equal(BookHoldStatus.Waiting, b1.Status);
        Assert.Equal(copyB.Id, b1.BookCopyId);
        Assert.Equal(BookHoldStatus.Waiting, (await ReloadAsync(holdB2.Id)).Status);
        Assert.Equal(1, await PositionAsync(other));
        Assert.Equal(2, await PositionAsync(third));
    }

    [Fact]
    public async Task ReaderCannotCancelAnotherReadersHoldOrTriggerPromotion()
    {
        var (owner, other) = await SeedReadersAsync();
        var third = await AddReaderAsync("third@example.com", "R-3");
        var book = await AddBookAsync("Sách A");
        var copy = await AddCopyAsync(book, "A-1", BookCopyStatus.OnHold);
        var heldAt = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);
        var othersHold = await AddHoldAsync(other, book, BookHoldStatus.Waiting, heldAt, copy: copy);
        var thirdHold = await AddHoldAsync(third, book, BookHoldStatus.Waiting, heldAt.AddHours(1));

        Assert.IsType<NotFoundObjectResult>(await CreateController(SignIn(owner)).CancelMyHoldApi(othersHold.Id));

        Assert.Equal(BookHoldStatus.Waiting, (await ReloadAsync(othersHold.Id)).Status);
        Assert.Equal(BookHoldStatus.Waiting, (await ReloadAsync(thirdHold.Id)).Status);
        Assert.Equal(BookCopyStatus.OnHold, (await ReloadCopyAsync(copy.Id)).Status);
    }

    private async Task<int?> PositionAsync(ReaderAccount reader) =>
        (await service.GetReaderHoldsAsync(reader.Id)).Single().QueuePosition;

    private async Task<DateTime> ExpectedPickupDeadlineUtcAsync()
    {
        var loans = new BookLoanService(db, new WorkingScheduleService(db));
        var date = await loans.AdjustDueDateAsync(
            DateOnly.FromDateTime(DateTime.Now).AddDays(ReaderRegistrationService.PickupDays));
        return date.ToDateTime(new TimeOnly(17, 0), DateTimeKind.Local).ToUniversalTime();
    }

    private async Task<BookCopy> AddCopyAsync(Book book, string code, string status)
    {
        var shelf = await db.Shelves.FirstOrDefaultAsync();
        if (shelf == null)
        {
            var warehouse = new Warehouse { Code = "KHO-1", Name = "Kho 1" };
            shelf = new Shelf { Warehouse = warehouse, Code = "KE-1", Name = "Kệ 1" };
            db.Shelves.Add(shelf);
            await db.SaveChangesAsync();
        }
        var copy = new BookCopy { BookId = book.Id, ShelfId = shelf.Id, CopyCode = code, Status = status };
        db.BookCopies.Add(copy);
        await db.SaveChangesAsync();
        return copy;
    }

    private async Task<BookCopy> ReloadCopyAsync(long copyId)
    {
        db.ChangeTracker.Clear();
        return await db.BookCopies.SingleAsync(copy => copy.Id == copyId);
    }

    private async Task<ReaderAccount> AddReaderAsync(string email, string code)
    {
        var reader = CreateReader(email, code);
        db.ReaderAccounts.Add(reader);
        await db.SaveChangesAsync();
        return reader;
    }

    private async Task<(ReaderAccount Owner, ReaderAccount Other)> SeedReadersAsync()
    {
        var owner = CreateReader("owner@example.com", "R-1");
        var other = CreateReader("other@example.com", "R-2");
        db.ReaderAccounts.AddRange(owner, other);
        await db.SaveChangesAsync();
        return (owner, other);
    }

    private static ReaderAccount CreateReader(string email, string code) => new()
    {
        FullName = "Reader " + code,
        DateOfBirth = new DateOnly(2000, 1, 1),
        Email = email,
        PhoneNumber = "111",
        StudentOrStaffCode = code,
        PasswordHash = "hash",
        Status = "Đang hoạt động"
    };

    private async Task<Book> AddBookAsync(string title)
    {
        var author = new Author { Name = "Tác giả " + title };
        var book = new Book { Title = title, Author = author };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book;
    }

    private async Task<BookHold> AddHoldAsync(ReaderAccount reader, Book book, string status, DateTime? heldAtUtc = null,
        DateTime? pickupDeadlineUtc = null, BookCopy? copy = null)
    {
        var hold = new BookHold
        {
            ReaderAccountId = reader.Id,
            BookId = book.Id,
            Status = status,
            HeldAtUtc = heldAtUtc ?? DateTime.UtcNow,
            PickupDeadlineUtc = pickupDeadlineUtc,
            BookCopyId = copy?.Id
        };
        db.BookHolds.Add(hold);
        await db.SaveChangesAsync();
        return hold;
    }

    private async Task<BookHold> ReloadAsync(long holdId)
    {
        db.ChangeTracker.Clear();
        return await db.BookHolds.SingleAsync(hold => hold.Id == holdId);
    }

    private string SignIn(ReaderAccount reader)
    {
        var context = new DefaultHttpContext();
        ReaderSessionCookies.Append(context, dataProtection, reader);
        return string.Join("; ", SetCookieHeaderValue.ParseList(context.Response.Headers.SetCookie.Select(value => value!).ToList())
            .Select(cookie => $"{cookie.Name}={cookie.Value}"));
    }

    private ReaderRegistrationController CreateController(string? cookieHeader)
    {
        var controller = new ReaderRegistrationController(service, new ReaderRegistrationIpRateLimiter(),
            new ReaderPasswordResetService(db, new PasswordHasher<ReaderAccount>(), new NoEmailSender(),
                NullLogger<ReaderPasswordResetService>.Instance),
            dataProtection, new AuditLogService(db, NullLogger<AuditLogService>.Instance), new NoOpEmailVerificationService());
        var context = new DefaultHttpContext();
        if (cookieHeader is not null) context.Request.Headers.Cookie = cookieHeader;
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        controller.Url = new StubUrlHelper();
        return controller;
    }

    private sealed class NoEmailSender : IEmailSender
    {
        public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();
        public string? Action(Microsoft.AspNetCore.Mvc.Routing.UrlActionContext actionContext) => "/ReaderRegistration/" + actionContext.Action;
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => null;
        public string? RouteUrl(Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext routeContext) => null;
    }
}
