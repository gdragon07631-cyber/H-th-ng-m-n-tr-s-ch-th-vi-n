using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;
using Project.Services;
using Xunit;

namespace Project.Tests;

public class WorkingScheduleTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Test1_KhaiBaoThuMoCua_ThanhCong()
    {
        using var db = CreateInMemoryDbContext();
        var service = new WorkingScheduleService(db);

        var result = await service.UpdateWeeklyScheduleAsync(DayOfWeek.Monday, true, "Mở cửa cả ngày");

        Assert.True(result.IsSuccess);
        Assert.True(result.Schedule!.IsOpen);
        Assert.Equal(DayOfWeek.Monday, result.Schedule.DayOfWeek);
    }

    [Fact]
    public async Task Test2_KhaiBaoThuDongCua_ThanhCong()
    {
        using var db = CreateInMemoryDbContext();
        var service = new WorkingScheduleService(db);

        var result = await service.UpdateWeeklyScheduleAsync(DayOfWeek.Sunday, false, "Nghỉ cuối tuần");

        Assert.True(result.IsSuccess);
        Assert.False(result.Schedule!.IsOpen);
    }

    [Fact]
    public async Task Test3_ThemNgayNghiCuThe_ThanhCong()
    {
        using var db = CreateInMemoryDbContext();
        var service = new WorkingScheduleService(db);
        var date = new DateOnly(2026, 9, 2);

        var result = await service.CreateHolidayClosureAsync(date, "Quốc khánh", "Đóng cửa cả ngày");

        Assert.True(result.IsSuccess);
        Assert.Equal(date, result.Holiday!.HolidayDate);
        Assert.Equal("Quốc khánh", result.Holiday.Reason);
    }

    [Fact]
    public async Task Test4_SuaNgayNghiCuThe_ThanhCong()
    {
        using var db = CreateInMemoryDbContext();
        var service = new WorkingScheduleService(db);
        var holiday = (await service.CreateHolidayClosureAsync(new DateOnly(2026, 5, 1), "Nghỉ lễ", null)).Holiday!;

        var result = await service.UpdateHolidayClosureAsync(holiday.Id, new DateOnly(2026, 5, 2), "Nghỉ bù", "Cập nhật ngày");

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 5, 2), result.Holiday!.HolidayDate);
        Assert.Equal("Nghỉ bù", result.Holiday.Reason);
    }

    [Fact]
    public async Task Test5_LichTuan_LuonCoDuBayThuVaDungTrangThaiSauKhiSua()
    {
        using var db = CreateInMemoryDbContext();
        var service = new WorkingScheduleService(db);

        await service.UpdateWeeklyScheduleAsync(DayOfWeek.Monday, false, null);
        await service.UpdateWeeklyScheduleAsync(DayOfWeek.Tuesday, true, null);
        var schedules = await service.GetWeeklySchedulesAsync();

        Assert.Equal(7, schedules.Count);
        Assert.Equal(7, schedules.Select(schedule => schedule.DayOfWeek).Distinct().Count());
        Assert.False(schedules.Single(schedule => schedule.DayOfWeek == DayOfWeek.Monday).IsOpen);
        Assert.True(schedules.Single(schedule => schedule.DayOfWeek == DayOfWeek.Tuesday).IsOpen);
    }

    [Fact]
    public async Task Test6_ThemHaiNgayNghiTrungNhau_ThatBai()
    {
        using var db = CreateInMemoryDbContext();
        var service = new WorkingScheduleService(db);
        var date = new DateOnly(2026, 12, 25);
        await service.CreateHolidayClosureAsync(date, "Giáng sinh", null);

        var result = await service.CreateHolidayClosureAsync(date, "Nghỉ khác", null);

        Assert.False(result.IsSuccess);
        Assert.Contains("đã có trong danh sách ngày nghỉ", result.ErrorMessage);
    }

    [Fact]
    public async Task Test7_NgayNghiGhiDeLichTuan()
    {
        using var db = CreateInMemoryDbContext();
        var service = new WorkingScheduleService(db);
        var date = new DateOnly(2026, 9, 2);
        await service.UpdateWeeklyScheduleAsync(date.DayOfWeek, true, null);
        await service.CreateHolidayClosureAsync(date, "Quốc khánh", null);

        var status = await service.GetStatusForDateAsync(date);

        Assert.False(status.IsOpen);
        Assert.True(status.IsHolidayClosure);
        Assert.Equal("Quốc khánh", status.Note);
    }
}
