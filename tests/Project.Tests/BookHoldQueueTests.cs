using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Tests;

/// <summary>Lát 2: thủ thư xem toàn bộ hàng đợi còn hiệu lực của một đầu sách.</summary>
public sealed class BookHoldQueueTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly ApplicationDbContext db;
    private readonly BookHoldQueueService service;

    public BookHoldQueueTests()
    {
        connection.Open();
        db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        service = new BookHoldQueueService(db);
    }

    [Fact]
    public async Task OneActiveHoldHasFirstPositionAndSavedPlacedTime()
    {
        var book = await AddBookAsync("Sách A");
        var reader = await AddReaderAsync("Bạn đọc A", "A");
        var placedAt = new DateTime(2026, 10, 1, 8, 30, 0, DateTimeKind.Utc);
        await AddHoldAsync(reader, book, BookHoldStatus.Waiting, placedAt);

        var item = Assert.Single(await service.GetActiveQueueForBookAsync(book.Id));

        Assert.Equal(1, item.Position);
        Assert.Equal("Bạn đọc A", item.ReaderName);
        Assert.Equal(placedAt, item.HeldAtUtc);
    }

    [Fact]
    public async Task QueueContainsEveryActiveHoldInFifoOrderAndDoesNotMutateData()
    {
        var book = await AddBookAsync("Sách A");
        var first = await AddReaderAsync("Bạn đọc 1", "R1");
        var second = await AddReaderAsync("Bạn đọc 2", "R2");
        var third = await AddReaderAsync("Bạn đọc 3", "R3");
        var placedAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        var firstHold = await AddHoldAsync(first, book, BookHoldStatus.Waiting, placedAt);
        var secondHold = await AddHoldAsync(second, book, BookHoldStatus.Available, placedAt.AddMinutes(5));
        var thirdHold = await AddHoldAsync(third, book, BookHoldStatus.Waiting, placedAt.AddMinutes(10));

        var queue = await service.GetActiveQueueForBookAsync(book.Id);

        Assert.Equal([firstHold.Id, secondHold.Id, thirdHold.Id], queue.Select(item => item.HoldId));
        Assert.Equal([1, 2, 3], queue.Select(item => item.Position));
        Assert.Equal([placedAt, placedAt.AddMinutes(5), placedAt.AddMinutes(10)], queue.Select(item => item.HeldAtUtc));
        Assert.Equal([firstHold.Id, secondHold.Id, thirdHold.Id],
            await db.BookHolds.OrderBy(hold => hold.HeldAtUtc).Select(hold => hold.Id).ToArrayAsync());
    }

    [Fact]
    public async Task QueueExcludesInactiveHolds()
    {
        var book = await AddBookAsync("Sách A");
        var active = await AddReaderAsync("Đang chờ", "Active");
        var cancelled = await AddReaderAsync("Đã hủy", "Cancelled");
        var loaned = await AddReaderAsync("Đã nhận", "Loaned");
        var placedAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        var activeHold = await AddHoldAsync(active, book, BookHoldStatus.Waiting, placedAt);
        await AddHoldAsync(cancelled, book, BookHoldStatus.Cancelled, placedAt.AddMinutes(1));
        await AddHoldAsync(loaned, book, BookHoldStatus.ConvertedToLoan, placedAt.AddMinutes(2));

        var queue = await service.GetActiveQueueForBookAsync(book.Id);

        Assert.Equal(activeHold.Id, Assert.Single(queue).HoldId);
    }

    [Fact]
    public async Task QueuesForDifferentBooksAreNotMixed()
    {
        var bookA = await AddBookAsync("Sách A");
        var bookB = await AddBookAsync("Sách B");
        var readerA = await AddReaderAsync("Bạn đọc A", "A");
        var readerB = await AddReaderAsync("Bạn đọc B", "B");
        var holdA = await AddHoldAsync(readerA, bookA, BookHoldStatus.Waiting, DateTime.UtcNow);
        await AddHoldAsync(readerB, bookB, BookHoldStatus.Waiting, DateTime.UtcNow.AddMinutes(-1));

        var queueA = await service.GetActiveQueueForBookAsync(bookA.Id);

        Assert.Equal(holdA.Id, Assert.Single(queueA).HoldId);
    }

    [Fact]
    public async Task EachStatusFilterReturnsOnlyItsMappedHold()
    {
        var book = await AddBookAsync("Sách có đủ trạng thái");
        var queued = await AddReaderAsync("Đang xếp hàng", "Queued");
        var pickup = await AddReaderAsync("Đang chờ nhận", "Pickup");
        var loaned = await AddReaderAsync("Đã mượn", "Loaned");
        var cancelled = await AddReaderAsync("Đã hủy", "Cancelled2");
        var start = new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc);
        var queuedHold = await AddHoldAsync(queued, book, BookHoldStatus.Waiting, start);
        var pickupHold = await AddHoldAsync(pickup, book, BookHoldStatus.Available, start.AddMinutes(1));
        var loanedHold = await AddHoldAsync(loaned, book, BookHoldStatus.ConvertedToLoan, start.AddMinutes(2));
        var cancelledHold = await AddHoldAsync(cancelled, book, BookHoldStatus.Cancelled, start.AddMinutes(3));

        Assert.Equal(queuedHold.Id, Assert.Single(await service.GetQueueForBookAsync(book.Id, BookHoldQueueFilter.Queued)).HoldId);
        Assert.Equal(pickupHold.Id, Assert.Single(await service.GetQueueForBookAsync(book.Id, BookHoldQueueFilter.WaitingPickup)).HoldId);
        Assert.Equal(loanedHold.Id, Assert.Single(await service.GetQueueForBookAsync(book.Id, BookHoldQueueFilter.ConvertedToLoan)).HoldId);
        Assert.Equal(cancelledHold.Id, Assert.Single(await service.GetQueueForBookAsync(book.Id, BookHoldQueueFilter.Cancelled)).HoldId);
    }

    [Fact]
    public async Task EmptyStatusFilterReturnsEmptyList()
    {
        var book = await AddBookAsync("Sách trống trạng thái");
        var reader = await AddReaderAsync("Bạn đọc", "Empty");
        await AddHoldAsync(reader, book, BookHoldStatus.Cancelled, DateTime.UtcNow);

        var queue = await service.GetQueueForBookAsync(book.Id, BookHoldQueueFilter.WaitingPickup);

        Assert.Empty(queue);
    }

    [Fact]
    public async Task SwitchingFiltersAndClearingFilterReturnsCurrentDataInOriginalOrder()
    {
        var book = await AddBookAsync("Sách chuyển lọc");
        var readers = new[]
        {
            await AddReaderAsync("Một", "Switch1"), await AddReaderAsync("Hai", "Switch2"),
            await AddReaderAsync("Ba", "Switch3"), await AddReaderAsync("Bốn", "Switch4")
        };
        var start = new DateTime(2026, 10, 3, 8, 0, 0, DateTimeKind.Utc);
        var holds = new[]
        {
            await AddHoldAsync(readers[0], book, BookHoldStatus.Waiting, start),
            await AddHoldAsync(readers[1], book, BookHoldStatus.Available, start.AddMinutes(1)),
            await AddHoldAsync(readers[2], book, BookHoldStatus.ConvertedToLoan, start.AddMinutes(2)),
            await AddHoldAsync(readers[3], book, BookHoldStatus.Cancelled, start.AddMinutes(3))
        };

        Assert.Equal(BookHoldStatus.Waiting, Assert.Single(await service.GetQueueForBookAsync(book.Id, BookHoldQueueFilter.Queued)).Status);
        Assert.Equal(BookHoldStatus.Available, Assert.Single(await service.GetQueueForBookAsync(book.Id, BookHoldQueueFilter.WaitingPickup)).Status);
        Assert.Equal(BookHoldStatus.ConvertedToLoan, Assert.Single(await service.GetQueueForBookAsync(book.Id, BookHoldQueueFilter.ConvertedToLoan)).Status);

        var all = await service.GetQueueForBookAsync(book.Id, BookHoldQueueFilter.All);
        Assert.Equal(holds.Select(hold => hold.Id), all.Select(item => item.HoldId));
        Assert.Equal([1, 2, 3, 4], all.Select(item => item.Position));
        Assert.Equal(holds.Select(hold => hold.HeldAtUtc), all.Select(item => item.HeldAtUtc));
    }

    private async Task<Book> AddBookAsync(string title)
    {
        var author = new Author { Name = "Tác giả " + title };
        var book = new Book { Title = title, Author = author };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book;
    }

    private async Task<ReaderAccount> AddReaderAsync(string name, string code)
    {
        var reader = new ReaderAccount
        {
            FullName = name, DateOfBirth = new DateOnly(2000, 1, 1), Email = code + "@example.com",
            PhoneNumber = "0900000000", StudentOrStaffCode = code, PasswordHash = "hash", Status = "Đang hoạt động"
        };
        db.ReaderAccounts.Add(reader);
        await db.SaveChangesAsync();
        return reader;
    }

    private async Task<BookHold> AddHoldAsync(ReaderAccount reader, Book book, string status, DateTime heldAtUtc)
    {
        var hold = new BookHold { ReaderAccountId = reader.Id, BookId = book.Id, Status = status, HeldAtUtc = heldAtUtc };
        db.BookHolds.Add(hold);
        await db.SaveChangesAsync();
        return hold;
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }
}
