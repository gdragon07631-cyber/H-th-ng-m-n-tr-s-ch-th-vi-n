using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Tests;

public sealed class LoanContactHistoryTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly ApplicationDbContext db;
    private readonly LoanContactHistoryService contacts;

    public LoanContactHistoryTests()
    {
        connection.Open();
        db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        contacts = new LoanContactHistoryService(db);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    [Fact]
    public async Task AddContactCreatesSeparateRecordWithNoteTimestampAndStaff()
    {
        var loan = await AddLoanAsync();
        var staff = await AddStaffAsync("Thủ thư An");
        var before = DateTime.UtcNow;

        var result = await contacts.AddAsync(loan.Id, "Đã gọi điện nhưng không nghe máy.", staff);

        var saved = Assert.Single(await contacts.GetByLoanIdAsync(loan.Id));
        Assert.True(result.IsSuccess);
        Assert.Equal("Đã gọi điện nhưng không nghe máy.", saved.Note);
        Assert.Equal(staff.Id, saved.ContactedByAdminAccountId);
        Assert.Equal("Thủ thư An", saved.ContactedBy);
        Assert.InRange(saved.CreatedAtUtc, before, DateTime.UtcNow);
    }

    [Fact]
    public async Task MultipleContactsAreKeptAndReturnedNewestFirst()
    {
        var loan = await AddLoanAsync();
        var staff = await AddStaffAsync("Thủ thư An");

        var first = await contacts.AddAsync(loan.Id, "Lần đầu", staff);
        var second = await contacts.AddAsync(loan.Id, "Lần hai", staff);
        var items = await contacts.GetByLoanIdAsync(loan.Id);

        Assert.Equal(2, items.Count);
        Assert.Equal([second.Item!.Id, first.Item!.Id], items.Select(item => item.Id));
        Assert.Equal(["Lần hai", "Lần đầu"], items.Select(item => item.Note));
    }

    [Fact]
    public async Task OverdueListShowsTheLatestContactWithoutChangingRangeOrOverdueDays()
    {
        var loan = await AddLoanAsync();
        var staff = await AddStaffAsync("Thủ thư An");
        await contacts.AddAsync(loan.Id, "Ghi chú cũ", staff);
        var latest = await contacts.AddAsync(loan.Id, "Ghi chú mới", staff);
        var loans = new BookLoanService(db, new WorkingScheduleService(db));
        var today = loan.DueDate.AddDays(3);

        var item = Assert.Single(await loans.GetOverdueAsync(today, OverdueLoanRange.OneToSevenDays));

        Assert.Equal(3, item.DaysOverdue);
        Assert.NotNull(item.LatestContact);
        Assert.Equal(latest.Item!.Id, item.LatestContact!.Id);
        Assert.Equal("Ghi chú mới", item.LatestContact.Note);
    }

    private async Task<AdminAccount> AddStaffAsync(string name)
    {
        var staff = new AdminAccount { FullName = name, Email = $"{Guid.NewGuid():N}@example.test", PasswordHash = "x", Role = AccountRoles.Librarian };
        db.AdminAccounts.Add(staff);
        await db.SaveChangesAsync();
        return staff;
    }

    private async Task<BookLoan> AddLoanAsync()
    {
        var author = new Author { Name = $"Tác giả {Guid.NewGuid():N}" };
        var book = new Book { Title = $"Sách {Guid.NewGuid():N}", Author = author };
        var reader = new ReaderAccount
        {
            FullName = "Bạn đọc", DateOfBirth = new DateOnly(2000, 1, 1), Email = $"{Guid.NewGuid():N}@example.test",
            PhoneNumber = "0900000000", StudentOrStaffCode = Guid.NewGuid().ToString("N"), PasswordHash = "x", Status = "Đang hoạt động"
        };
        db.AddRange(book, reader);
        await db.SaveChangesAsync();
        var dueDate = new DateOnly(2026, 10, 9);
        var loan = new BookLoan { BookId = book.Id, ReaderAccountId = reader.Id, LoanDate = dueDate.AddDays(-14), OriginalDueDate = dueDate, DueDate = dueDate };
        db.BookLoans.Add(loan);
        await db.SaveChangesAsync();
        return loan;
    }
}
