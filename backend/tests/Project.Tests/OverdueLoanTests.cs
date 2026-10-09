using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Tests;

public sealed class OverdueLoanTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 9);
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly ApplicationDbContext db;
    private readonly BookLoanService service;
    private int sequence;

    public OverdueLoanTests()
    {
        connection.Open();
        db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        service = new BookLoanService(db, new WorkingScheduleService(db));
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    [Fact]
    public async Task OneOverdueLoanReturnsReaderContactCardBookAndDaysOverdue()
    {
        var reader = await AddReaderAsync("Nguyễn Văn An", "0901234567", "CARD-AN");
        var loan = await AddLoanAsync(reader, "Lập trình C#", Today.AddDays(-4));

        var item = Assert.Single(await service.GetOverdueAsync(Today));

        Assert.Equal(loan.Id, item.LoanId);
        Assert.Equal(4, item.DaysOverdue);
        Assert.Equal("Nguyễn Văn An", item.ReaderName);
        Assert.Equal("0901234567", item.PhoneNumber);
        Assert.Equal("CARD-AN", item.CardCode);
        Assert.Equal("Lập trình C#", item.BookTitle);
    }

    [Fact]
    public async Task MultipleOverdueLoansAreAllReturnedOrderedFromLongestDelay()
    {
        var reader = await AddReaderAsync("Bạn đọc", "0900000000", "CARD-01");
        var one = await AddLoanAsync(reader, "Trễ 1", Today.AddDays(-1));
        var ten = await AddLoanAsync(reader, "Trễ 10", Today.AddDays(-10));
        var three = await AddLoanAsync(reader, "Trễ 3", Today.AddDays(-3));

        var items = await service.GetOverdueAsync(Today);

        Assert.Equal([ten.Id, three.Id, one.Id], items.Select(item => item.LoanId));
        Assert.Equal([10, 3, 1], items.Select(item => item.DaysOverdue));
    }

    [Fact]
    public async Task ReaderAndBookInformationIsNotMixedBetweenLoans()
    {
        var an = await AddReaderAsync("Nguyễn Văn An", "0901111111", "CARD-AN");
        var binh = await AddReaderAsync("Trần Thị Bình", "0902222222", "CARD-BINH");
        await AddLoanAsync(an, "Sách của An", Today.AddDays(-2));
        await AddLoanAsync(binh, "Sách của Bình", Today.AddDays(-5));

        var items = await service.GetOverdueAsync(Today);

        Assert.Contains(items, item => item.ReaderName == "Nguyễn Văn An" && item.PhoneNumber == "0901111111" && item.CardCode == "CARD-AN" && item.BookTitle == "Sách của An");
        Assert.Contains(items, item => item.ReaderName == "Trần Thị Bình" && item.PhoneNumber == "0902222222" && item.CardCode == "CARD-BINH" && item.BookTitle == "Sách của Bình");
    }

    [Fact]
    public async Task DueTodayFutureAndReturnedLoansAreNotReturned()
    {
        var reader = await AddReaderAsync("Bạn đọc", "0900000000", "CARD-01");
        await AddLoanAsync(reader, "Đến hạn hôm nay", Today);
        await AddLoanAsync(reader, "Chưa đến hạn", Today.AddDays(1));
        var overdue = await AddLoanAsync(reader, "Đã quá hạn", Today.AddDays(-1));
        var returned = await AddLoanAsync(reader, "Đã trả", Today.AddDays(-2));
        returned.Status = "Đã trả";

        var items = await service.GetOverdueAsync(Today);

        Assert.Equal(overdue.Id, Assert.Single(items).LoanId);
    }

    [Fact]
    public async Task SameDaysOverdueUsesDueDateThenLoanIdAsStableOrder()
    {
        var reader = await AddReaderAsync("Bạn đọc", "0900000000", "CARD-01");
        var first = await AddLoanAsync(reader, "Sách 1", Today.AddDays(-2));
        var second = await AddLoanAsync(reader, "Sách 2", Today.AddDays(-2));

        var items = await service.GetOverdueAsync(Today);

        Assert.Equal([first.Id, second.Id], items.Select(item => item.LoanId));
    }

    private async Task<ReaderAccount> AddReaderAsync(string name, string phone, string cardCode)
    {
        var id = ++sequence;
        var cardType = await db.LibraryCardTypes.FirstOrDefaultAsync();
        if (cardType is null)
        {
            cardType = new LibraryCardType { Name = "Chuẩn" };
            db.LibraryCardTypes.Add(cardType);
            await db.SaveChangesAsync();
        }
        var reader = new ReaderAccount
        {
            FullName = name, PhoneNumber = phone, DateOfBirth = new DateOnly(2000, 1, 1),
            Email = $"reader{id}@example.test", StudentOrStaffCode = $"SV{id}", PasswordHash = "x", Status = "Đang hoạt động",
            LibraryCard = new LibraryCard { CardCode = cardCode, LibraryCardTypeId = cardType.Id, IssuedOn = Today.AddYears(-1), ExpiresOn = Today.AddYears(1), Status = "Đang hoạt động" }
        };
        db.ReaderAccounts.Add(reader);
        await db.SaveChangesAsync();
        return reader;
    }

    private async Task<BookLoan> AddLoanAsync(ReaderAccount reader, string title, DateOnly dueDate)
    {
        var author = await db.Authors.FirstOrDefaultAsync() ?? new Author { Name = "Tác giả" };
        if (author.Id == 0) db.Authors.Add(author);
        var book = new Book { Title = title, Author = author };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        var loan = new BookLoan { BookId = book.Id, ReaderAccountId = reader.Id, LoanDate = dueDate.AddDays(-14), OriginalDueDate = dueDate, DueDate = dueDate };
        db.BookLoans.Add(loan);
        await db.SaveChangesAsync();
        return loan;
    }

}
