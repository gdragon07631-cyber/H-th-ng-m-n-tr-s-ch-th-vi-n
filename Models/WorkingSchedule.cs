using System.ComponentModel.DataAnnotations;

namespace Project.Models;

public sealed class WeeklyWorkingSchedule
{
    public int Id { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public bool IsOpen { get; set; } = true;

    [MaxLength(500)]
    public string? Note { get; set; }
}

public sealed class HolidayClosure
{
    public int Id { get; set; }
    public DateOnly HolidayDate { get; set; }

    [Required, MaxLength(150)]
    public string Reason { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Note { get; set; }
}

public sealed class UpdateWeeklyWorkingScheduleViewModel
{
    [Range(0, 6, ErrorMessage = "Thứ trong tuần không hợp lệ.")]
    public DayOfWeek DayOfWeek { get; set; }
    public bool IsOpen { get; set; }

    [MaxLength(500, ErrorMessage = "Ghi chú không được vượt quá 500 ký tự.")]
    public string? Note { get; set; }
}

public class HolidayClosureInputViewModel
{
    [Required(ErrorMessage = "Vui lòng chọn ngày nghỉ.")]
    public DateOnly? HolidayDate { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên hoặc lý do nghỉ.")]
    [MaxLength(150, ErrorMessage = "Tên/lý do nghỉ không được vượt quá 150 ký tự.")]
    public string Reason { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "Ghi chú không được vượt quá 500 ký tự.")]
    public string? Note { get; set; }
}

public sealed class EditHolidayClosureViewModel : HolidayClosureInputViewModel
{
    [Required]
    public int Id { get; set; }
}

public sealed class WorkingScheduleIndexViewModel
{
    public IReadOnlyList<WeeklyWorkingSchedule> WeeklySchedules { get; set; } = [];
    public IReadOnlyList<HolidayClosure> HolidayClosures { get; set; } = [];
    public HolidayClosureInputViewModel NewHoliday { get; set; } = new();
}
