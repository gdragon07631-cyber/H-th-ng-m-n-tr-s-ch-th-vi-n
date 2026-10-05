using Project.Services;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;
using Xunit;

namespace Project.Tests;

public class DueDateAdjusterTests
{
    [Fact]
    public void Test1_HanTraNgayMoCua_GiuNguyen()
    {
        var proposed = new DateOnly(2026, 9, 1);
        var result = DueDateAdjuster.AdjustDueDate(proposed, _ => true);
        Assert.Equal(proposed, result);
    }

    [Fact]
    public void Test2_DongCuaTheoLichTuan_DaySangNgayMoKeTiep()
    {
        var sunday = new DateOnly(2026, 9, 6);
        var result = DueDateAdjuster.AdjustDueDate(sunday, date => date.DayOfWeek != DayOfWeek.Sunday);
        Assert.Equal(new DateOnly(2026, 9, 7), result);
    }

    [Fact]
    public void Test3_NgayNghiCuThe_GhiDeNgayThuongMoCua()
    {
        var holiday = new DateOnly(2026, 9, 2);
        var result = DueDateAdjuster.AdjustDueDate(holiday, date => date != holiday);
        Assert.Equal(new DateOnly(2026, 9, 3), result);
    }

    [Fact]
    public void Test4_ChuoiNhieuNgayDongCua_NhayDenNgayMoDauTien()
    {
        var start = new DateOnly(2026, 4, 30);
        var closedDays = new HashSet<DateOnly>
        {
            start, start.AddDays(1), start.AddDays(2), start.AddDays(3)
        };
        var result = DueDateAdjuster.AdjustDueDate(start, date => !closedDays.Contains(date));
        Assert.Equal(start.AddDays(4), result);
    }

    [Fact]
    public void Test5_KhongCoNgayMoTrongGioiHan_BaoLoiRoRang()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DueDateAdjuster.AdjustDueDate(new DateOnly(2026, 1, 1), _ => false, 2));
        Assert.Contains("Không tìm thấy ngày mở cửa", exception.Message);
    }

    [Fact]
    public async Task Test6_TaoPhieuMuon_LuuHanTraDaDieuChinhTheoLich()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var db = new ApplicationDbContext(options);
        var calendar = new WorkingScheduleService(db);
        await calendar.UpdateWeeklyScheduleAsync(DayOfWeek.Sunday, false, null);
        var sunday = new DateOnly(2026, 9, 6);
        await calendar.CreateHolidayClosureAsync(sunday.AddDays(15), "Ngày nghỉ đặc biệt", null);

        var book = new Book { Title = "Sách kiểm thử", AuthorId = 1 };
        var reader = new ReaderAccount
        {
            FullName = "Bạn đọc kiểm thử", DateOfBirth = new DateOnly(2000, 1, 1),
            Email = "reader@example.test", PhoneNumber = "0900000000", StudentOrStaffCode = "TEST-01",
            PasswordHash = "hash", Status = "Đang hoạt động"
        };
        db.Books.Add(book);
        db.ReaderAccounts.Add(reader);
        await db.SaveChangesAsync();

        var service = new BookLoanService(db, calendar);
        var result = await service.CreateAsync(book.Id, reader.Id, sunday.AddDays(-14));

        Assert.True(result.IsSuccess);
        Assert.Equal(sunday.AddDays(1), result.Loan!.DueDate);
        Assert.Equal(sunday, result.Loan.OriginalDueDate);
    }

    [Fact]
    public async Task Renew_ValidLoan_AddsSevenDaysAndPersists()
    {
        var (db, service) = CreateRenewalService();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var loan = await CreateRenewableLoan(db, today.AddDays(-1), today.AddDays(20), renewalCount: 1);

        var result = await service.RenewAsync(loan.Id, today);

        Assert.True(result.IsSuccess);
        Assert.Equal(today.AddDays(20), result.OldDueDate);
        Assert.Equal(today.AddDays(27), result.Loan!.DueDate);
        Assert.Equal(2, result.Loan.RenewalCount);
        Assert.Equal(today.AddDays(27), (await db.BookLoans.FindAsync(loan.Id))!.DueDate);
    }

    [Fact]
    public async Task Renew_ClosedDayMovesToNextOpenDay()
    {
        var (db, service) = CreateRenewalService();
        await new WorkingScheduleService(db).UpdateWeeklyScheduleAsync(DayOfWeek.Sunday, false, null);
        var today = DateOnly.FromDateTime(DateTime.Today);
        // Pick a Sunday far enough ahead that the current due date is not already overdue, whatever today is.
        var sunday = today.AddDays(BookLoanService.DefaultRenewalDays);
        while (sunday.DayOfWeek != DayOfWeek.Sunday) sunday = sunday.AddDays(1);
        var dueDate = sunday.AddDays(-BookLoanService.DefaultRenewalDays);
        var loan = await CreateRenewableLoan(db, today, dueDate);

        var result = await service.RenewAsync(loan.Id, today);

        Assert.True(result.IsSuccess);
        Assert.Equal(sunday.AddDays(1), result.Loan!.DueDate);
        Assert.Equal(1, result.Loan.RenewalCount);
        Assert.Equal(sunday.AddDays(1), (await db.BookLoans.FindAsync(loan.Id))!.DueDate);
    }

    [Fact]
    public async Task Renew_ExpiredLoan_IsRejectedWithoutChangingDueDate()
    {
        var (db, service) = CreateRenewalService();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var loan = await CreateRenewableLoan(db, today.AddDays(-20), today.AddDays(-1));

        var result = await service.RenewAsync(loan.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Contains("quá hạn", result.ErrorMessage);
        Assert.Equal(today.AddDays(-1), (await db.BookLoans.FindAsync(loan.Id))!.DueDate);
    }

    [Fact]
    public async Task Renew_AtCardLimit_IsRejectedWithoutChangingLoan()
    {
        var (db, service) = CreateRenewalService();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var loan = await CreateRenewableLoan(db, today.AddDays(-1), today.AddDays(20), renewalCount: 3);

        var result = await service.RenewAsync(loan.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Contains("sử dụng hết số lần gia hạn", result.ErrorMessage);
        Assert.Equal(today.AddDays(20), (await db.BookLoans.FindAsync(loan.Id))!.DueDate);
        Assert.Equal(3, loan.RenewalCount);
    }

    [Fact]
    public async Task Renew_WithAnotherOverdueLoan_IsRejectedWithoutChangingCurrentLoan()
    {
        var (db, service) = CreateRenewalService();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var loan = await CreateRenewableLoan(db, today.AddDays(-1), today.AddDays(20), renewalCount: 1);
        db.BookLoans.Add(new BookLoan
        {
            ReaderAccountId = loan.ReaderAccountId, LoanDate = today.AddDays(-30),
            OriginalDueDate = today.AddDays(-2), DueDate = today.AddDays(-2)
        });
        await db.SaveChangesAsync();

        var result = await service.RenewAsync(loan.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Equal("OTHER_OVERDUE_LOAN", result.ReasonCode);
        Assert.Contains("phiếu mượn khác quá hạn", result.ErrorMessage);
        Assert.Equal(today.AddDays(20), loan.DueDate);
        Assert.Equal(1, loan.RenewalCount);
    }

    [Fact]
    public async Task Renew_WithOutstandingBalance_IsRejectedWithoutChangingCurrentLoan()
    {
        var (db, service) = CreateRenewalService();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var loan = await CreateRenewableLoan(db, today.AddDays(-1), today.AddDays(20), renewalCount: 1);
        var reader = await db.ReaderAccounts.FindAsync(loan.ReaderAccountId);
        reader!.OutstandingBalance = 50000m;
        await db.SaveChangesAsync();

        var result = await service.RenewAsync(loan.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Equal("UNPAID_FEE", result.ReasonCode);
        Assert.Contains("đang còn phí/phạt chưa thanh toán", result.ErrorMessage);
        Assert.Equal(today.AddDays(20), loan.DueDate);
        Assert.Equal(1, loan.RenewalCount);
    }

    [Fact]
    public async Task Renew_WithOverdueLoanAndDebt_ReturnsBothReasons()
    {
        var (db, service) = CreateRenewalService();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var loan = await CreateRenewableLoan(db, today.AddDays(-1), today.AddDays(20), renewalCount: 1);
        db.BookLoans.Add(new BookLoan
        {
            ReaderAccountId = loan.ReaderAccountId, LoanDate = today.AddDays(-30),
            OriginalDueDate = today.AddDays(-2), DueDate = today.AddDays(-2)
        });
        var reader = await db.ReaderAccounts.FindAsync(loan.ReaderAccountId);
        reader!.OutstandingBalance = 50000m;
        await db.SaveChangesAsync();

        var result = await service.RenewAsync(loan.Id, today);

        Assert.False(result.IsSuccess);
        Assert.Equal("OTHER_OVERDUE_LOAN_AND_UNPAID_FEE", result.ReasonCode);
        Assert.Contains("quá hạn và còn phí/phạt", result.ErrorMessage);
        Assert.Equal(today.AddDays(20), loan.DueDate);
        Assert.Equal(1, loan.RenewalCount);
    }

    private static (ApplicationDbContext Db, BookLoanService Service) CreateRenewalService()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new ApplicationDbContext(options);
        return (db, new BookLoanService(db, new WorkingScheduleService(db)));
    }

    private static async Task<BookLoan> CreateRenewableLoan(
        ApplicationDbContext db, DateOnly loanDate, DateOnly dueDate, int renewalCount = 0)
    {
        var cardType = new LibraryCardType { Name = $"Test-{Guid.NewGuid()}", MaxRenewals = 3 };
        var reader = new ReaderAccount
        {
            FullName = "Test Reader", DateOfBirth = new DateOnly(2000, 1, 1), Email = $"{Guid.NewGuid()}@example.test",
            PhoneNumber = "0900000000", StudentOrStaffCode = Guid.NewGuid().ToString(), PasswordHash = "hash",
            Status = "Đang hoạt động",
            LibraryCard = new LibraryCard
            {
                CardCode = Guid.NewGuid().ToString("N"), LibraryCardType = cardType,
                IssuedOn = loanDate, ExpiresOn = dueDate.AddYears(1), Status = "Đang hoạt động"
            }
        };
        db.ReaderAccounts.Add(reader);
        await db.SaveChangesAsync();
        var loan = new BookLoan
        {
            ReaderAccountId = reader.Id, LoanDate = loanDate, OriginalDueDate = dueDate,
            DueDate = dueDate, RenewalCount = renewalCount
        };
        db.BookLoans.Add(loan);
        await db.SaveChangesAsync();
        return loan;
    }
}
