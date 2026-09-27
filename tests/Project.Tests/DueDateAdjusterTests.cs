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
}
