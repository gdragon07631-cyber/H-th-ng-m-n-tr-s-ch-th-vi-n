using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

public sealed class WorkingScheduleController(IWorkingScheduleService schedules, ApplicationDbContext dbContext) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct = default)
    {
        if (!await IsLibrarianSignedInAsync(ct)) return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Index)) });
        return View(await BuildIndexModel(ct));
    }

    private async Task<bool> IsLibrarianSignedInAsync(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue("admin_refresh", out var token) || string.IsNullOrWhiteSpace(token)) return false;
        var hash = TokenService.HashRefreshToken(token);
        return await dbContext.RefreshTokens.AnyAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTime.UtcNow && item.AdminAccount.IsActive && item.AdminAccount.Role == AccountRoles.SystemAdmin, ct);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateWeekly(UpdateWeeklyWorkingScheduleViewModel model, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = FirstModelError();
            return RedirectToAction(nameof(Index));
        }

        var result = await schedules.UpdateWeeklyScheduleAsync(model.DayOfWeek, model.IsOpen, model.Note, ct);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? "Đã cập nhật lịch làm việc."
            : result.ErrorMessage;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateHoliday(HolidayClosureInputViewModel model, CancellationToken ct = default)
    {
        if (!ModelState.IsValid || model.HolidayDate == null)
        {
            TempData["ErrorMessage"] = FirstModelError();
            return RedirectToAction(nameof(Index));
        }

        var result = await schedules.CreateHolidayClosureAsync(model.HolidayDate.Value, model.Reason, model.Note, ct);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? "Đã thêm ngày nghỉ cụ thể."
            : result.ErrorMessage;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditHoliday(EditHolidayClosureViewModel model, CancellationToken ct = default)
    {
        if (!ModelState.IsValid || model.HolidayDate == null)
        {
            TempData["ErrorMessage"] = FirstModelError();
            return RedirectToAction(nameof(Index));
        }

        var result = await schedules.UpdateHolidayClosureAsync(model.Id, model.HolidayDate.Value, model.Reason, model.Note, ct);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? "Đã cập nhật ngày nghỉ cụ thể."
            : result.ErrorMessage;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteHoliday(int id, CancellationToken ct = default)
    {
        var result = await schedules.DeleteHolidayClosureAsync(id, ct);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? "Đã xóa ngày nghỉ cụ thể."
            : result.ErrorMessage;
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> HolidayDetails(int id, CancellationToken ct = default)
    {
        var holiday = await schedules.GetHolidayClosureByIdAsync(id, ct);
        if (holiday == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy ngày nghỉ.";
            return RedirectToAction(nameof(Index));
        }
        return View(holiday);
    }

    [HttpGet("api/working-schedule/weekly")]
    public async Task<IActionResult> GetWeeklyApi(CancellationToken ct = default) =>
        Ok((await schedules.GetWeeklySchedulesAsync(ct)).Select(ToWeeklyApiModel));

    [HttpPut("api/working-schedule/weekly/{dayOfWeek:int}")]
    public async Task<IActionResult> UpdateWeeklyApi(int dayOfWeek, [FromBody] UpdateWeeklyWorkingScheduleViewModel? model, CancellationToken ct = default)
    {
        if (model == null || dayOfWeek is < 0 or > 6 || (int)model.DayOfWeek != dayOfWeek || !TryValidateModel(model))
            return BadRequest(new { message = "Dữ liệu lịch tuần không hợp lệ." });

        var result = await schedules.UpdateWeeklyScheduleAsync(model.DayOfWeek, model.IsOpen, model.Note, ct);
        if (!result.IsSuccess) return BadRequest(new { message = result.ErrorMessage });
        return Ok(ToWeeklyApiModel(result.Schedule!));
    }

    [HttpGet("api/holiday-closures")]
    public async Task<IActionResult> GetHolidaysApi(CancellationToken ct = default) =>
        Ok((await schedules.GetHolidayClosuresAsync(ct)).Select(ToHolidayApiModel));

    [HttpGet("api/working-schedule/date/{date}")]
    public async Task<IActionResult> GetStatusForDateApi(DateOnly date, CancellationToken ct = default)
    {
        var status = await schedules.GetStatusForDateAsync(date, ct);
        return Ok(new { date, status.IsOpen, status.IsHolidayClosure, status.Note });
    }

    [HttpGet("api/holiday-closures/{id:int}")]
    public async Task<IActionResult> GetHolidayApi(int id, CancellationToken ct = default)
    {
        var holiday = await schedules.GetHolidayClosureByIdAsync(id, ct);
        return holiday == null ? NotFound(new { message = "Không tìm thấy ngày nghỉ." }) : Ok(ToHolidayApiModel(holiday));
    }

    [HttpPost("api/holiday-closures")]
    public async Task<IActionResult> CreateHolidayApi([FromBody] HolidayClosureInputViewModel? model, CancellationToken ct = default)
    {
        if (model?.HolidayDate == null || !TryValidateModel(model))
            return BadRequest(new { message = "Dữ liệu ngày nghỉ không hợp lệ." });
        var result = await schedules.CreateHolidayClosureAsync(model.HolidayDate.Value, model.Reason, model.Note, ct);
        if (!result.IsSuccess) return Conflict(new { message = result.ErrorMessage });
        return Created($"/api/holiday-closures/{result.Holiday!.Id}", ToHolidayApiModel(result.Holiday));
    }

    [HttpPut("api/holiday-closures/{id:int}")]
    public async Task<IActionResult> UpdateHolidayApi(int id, [FromBody] HolidayClosureInputViewModel? model, CancellationToken ct = default)
    {
        if (model?.HolidayDate == null || !TryValidateModel(model))
            return BadRequest(new { message = "Dữ liệu ngày nghỉ không hợp lệ." });
        var result = await schedules.UpdateHolidayClosureAsync(id, model.HolidayDate.Value, model.Reason, model.Note, ct);
        if (!result.IsSuccess)
            return result.ErrorMessage == "Không tìm thấy ngày nghỉ."
                ? NotFound(new { message = result.ErrorMessage })
                : Conflict(new { message = result.ErrorMessage });
        return Ok(ToHolidayApiModel(result.Holiday!));
    }

    [HttpDelete("api/holiday-closures/{id:int}")]
    public async Task<IActionResult> DeleteHolidayApi(int id, CancellationToken ct = default)
    {
        var result = await schedules.DeleteHolidayClosureAsync(id, ct);
        return result.IsSuccess ? Ok(new { message = "Đã xóa ngày nghỉ cụ thể." }) : NotFound(new { message = result.ErrorMessage });
    }

    private async Task<WorkingScheduleIndexViewModel> BuildIndexModel(CancellationToken ct) => new()
    {
        WeeklySchedules = await schedules.GetWeeklySchedulesAsync(ct),
        HolidayClosures = await schedules.GetHolidayClosuresAsync(ct)
    };

    private string FirstModelError() =>
        ModelState.Values.SelectMany(value => value.Errors).FirstOrDefault()?.ErrorMessage ?? "Dữ liệu không hợp lệ.";

    private static object ToWeeklyApiModel(WeeklyWorkingSchedule schedule) => new
    {
        dayOfWeek = (int)schedule.DayOfWeek,
        dayName = GetDayName(schedule.DayOfWeek),
        schedule.IsOpen,
        schedule.Note
    };

    private static object ToHolidayApiModel(HolidayClosure holiday) => new
    {
        holiday.Id,
        holiday.HolidayDate,
        holiday.Reason,
        holiday.Note,
        isOpen = false
    };

    public static string GetDayName(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => "Thứ Hai",
        DayOfWeek.Tuesday => "Thứ Ba",
        DayOfWeek.Wednesday => "Thứ Tư",
        DayOfWeek.Thursday => "Thứ Năm",
        DayOfWeek.Friday => "Thứ Sáu",
        DayOfWeek.Saturday => "Thứ Bảy",
        DayOfWeek.Sunday => "Chủ nhật",
        _ => "Không hợp lệ"
    };
}
