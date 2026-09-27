using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Services;

public sealed class BookLoanService(ApplicationDbContext db, IWorkingScheduleService workingScheduleService) : IBookLoanService
{
    public const int DefaultLoanDays = 14;

    public async Task<IReadOnlyList<BookLoan>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.BookLoans.Include(loan => loan.Book).Include(loan => loan.ReaderAccount)
            .OrderByDescending(loan => loan.CreatedAtUtc).ToListAsync(cancellationToken);

    public async Task<BookLoanOutcome> CreateAsync(int bookId, int readerAccountId, DateOnly loanDate, CancellationToken cancellationToken = default)
    {
        var book = await db.Books.FindAsync([bookId], cancellationToken);
        if (book == null) return new(false, "Không tìm thấy sách.");
        var reader = await db.ReaderAccounts.FindAsync([readerAccountId], cancellationToken);
        if (reader == null) return new(false, "Không tìm thấy bạn đọc.");
        if (!string.Equals(reader.Status, "Đang hoạt động", StringComparison.OrdinalIgnoreCase))
            return new(false, "Bạn đọc chưa ở trạng thái hoạt động.");

        var originalDueDate = loanDate.AddDays(DefaultLoanDays);
        DateOnly dueDate;
        try { dueDate = await AdjustDueDateAsync(originalDueDate, cancellationToken); }
        catch (InvalidOperationException exception) { return new(false, exception.Message); }

        var loan = new BookLoan
        {
            BookId = bookId,
            ReaderAccountId = readerAccountId,
            LoanDate = loanDate,
            OriginalDueDate = originalDueDate,
            DueDate = dueDate,
            CreatedAtUtc = DateTime.UtcNow,
            Book = book,
            ReaderAccount = reader
        };
        db.BookLoans.Add(loan);
        await db.SaveChangesAsync(cancellationToken);
        return new(true, Loan: loan);
    }

    public async Task<DateOnly> AdjustDueDateAsync(DateOnly proposedDate, CancellationToken cancellationToken = default)
    {
        var weeklySchedules = await workingScheduleService.GetWeeklySchedulesAsync(cancellationToken);
        var holidayDates = (await workingScheduleService.GetHolidayClosuresAsync(cancellationToken))
            .Select(holiday => holiday.HolidayDate).ToHashSet();
        var openByDay = weeklySchedules.ToDictionary(schedule => schedule.DayOfWeek, schedule => schedule.IsOpen);

        return DueDateAdjuster.AdjustDueDate(proposedDate,
            date => !holidayDates.Contains(date) && openByDay.TryGetValue(date.DayOfWeek, out var isOpen) && isOpen);
    }
}
