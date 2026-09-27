namespace Project.Services;

public static class DueDateAdjuster
{
    public static DateOnly AdjustDueDate(DateOnly proposedDate, Func<DateOnly, bool> isOpen, int maximumDaysToSearch = 366)
    {
        ArgumentNullException.ThrowIfNull(isOpen);
        for (var daysChecked = 0; daysChecked <= maximumDaysToSearch; daysChecked++)
        {
            var candidate = proposedDate.AddDays(daysChecked);
            if (isOpen(candidate)) return candidate;
        }

        throw new InvalidOperationException($"Không tìm thấy ngày mở cửa trong {maximumDaysToSearch} ngày tiếp theo.");
    }
}
