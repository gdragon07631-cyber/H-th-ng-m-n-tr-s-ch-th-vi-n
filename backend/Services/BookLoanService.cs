using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Services;

public sealed class BookLoanService(ApplicationDbContext db, IWorkingScheduleService workingScheduleService) : IBookLoanService
{
    public const int DefaultLoanDays = 14;
    public const int DefaultRenewalDays = 7;

    public async Task<IReadOnlyList<BookLoan>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.BookLoans.Include(loan => loan.Book)
            .Include(loan => loan.ReaderAccount).ThenInclude(reader => reader!.LibraryCard)
                .ThenInclude(card => card!.LibraryCardType)
            .OrderByDescending(loan => loan.CreatedAtUtc).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<BookLoan>> GetForReaderAsync(int readerAccountId, CancellationToken cancellationToken = default) =>
        await db.BookLoans.AsNoTracking().Where(loan => loan.ReaderAccountId == readerAccountId)
            .Include(loan => loan.Book)
            .Include(loan => loan.ReaderAccount).ThenInclude(reader => reader!.LibraryCard)
                .ThenInclude(card => card!.LibraryCardType)
            .OrderByDescending(loan => loan.CreatedAtUtc).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OverdueLoanItem>> GetOverdueAsync(DateOnly today, CancellationToken cancellationToken = default)
        => await GetOverdueAsync(today, OverdueLoanRange.All, cancellationToken);

    public async Task<IReadOnlyList<OverdueLoanItem>> GetOverdueAsync(
        DateOnly today, OverdueLoanRange range, CancellationToken cancellationToken = default)
    {
        var candidates = await db.BookLoans
            .Include(loan => loan.Book)
            .Include(loan => loan.BookCopy)
            .Include(loan => loan.ReaderAccount).ThenInclude(reader => reader!.LibraryCard)
            .Include(loan => loan.ContactHistories)
            .Where(loan => loan.DueDate < today)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0) return [];

        var firstCountedDate = candidates.Min(loan => loan.DueDate).AddDays(1);
        var openDates = await workingScheduleService.GetOpenDatesAsync(firstCountedDate, today, cancellationToken);

        return candidates
            .Where(loan => !loan.IsReturned)
            .Select(loan => new OverdueLoanItem(
                loan.Id,
                openDates.Count(date => date > loan.DueDate),
                loan.ReaderAccount?.FullName ?? string.Empty,
                loan.ReaderAccount?.PhoneNumber ?? string.Empty,
                loan.ReaderAccount?.LibraryCard?.CardCode,
                loan.Book?.Title ?? string.Empty,
                loan.LoanDate,
                loan.DueDate,
                loan.BookCopy?.CopyCode,
                loan.ContactHistories.OrderByDescending(history => history.CreatedAtUtc).ThenByDescending(history => history.Id)
                    .Select(history => new LoanContactHistoryItem(history.Id, history.BookLoanId, history.CreatedAtUtc,
                        history.Note, history.ContactedByAdminAccountId, history.ContactedBy)).FirstOrDefault()))
            .Where(item => item.DaysOverdue > 0)
            .Where(item => range switch
            {
                OverdueLoanRange.OneToSevenDays => item.DaysOverdue <= 7,
                OverdueLoanRange.MoreThanSevenDays => item.DaysOverdue > 7,
                OverdueLoanRange.MoreThanThirtyDays => item.DaysOverdue > 30,
                _ => true
            })
            .OrderByDescending(item => item.DaysOverdue)
            .ThenBy(item => item.DueDate)
            .ThenBy(item => item.LoanId)
            .ToList();
    }

    public Task<BookLoanOutcome> CreateAsync(int bookId, int readerAccountId, DateOnly loanDate, CancellationToken cancellationToken = default) =>
        CreateAsync(bookId, readerAccountId, loanDate, null, cancellationToken);

    public async Task<BookLoanOutcome> CreateAsync(int bookId, int readerAccountId, DateOnly loanDate, string? actor, CancellationToken cancellationToken = default)
    {
        var result = await CreateManyAsync([bookId], readerAccountId, loanDate, actor, cancellationToken);
        if (!result.IsSuccess)
        {
            return new(false, result.ErrorMessage);
        }
        return new(true, Loan: result.Loans.FirstOrDefault());
    }

    public Task<BatchBookLoanOutcome> CreateManyAsync(
        IReadOnlyList<int> bookIds, int readerAccountId, DateOnly loanDate, CancellationToken cancellationToken = default) =>
        CreateManyAsync(bookIds, readerAccountId, loanDate, null, cancellationToken);

    public async Task<BatchBookLoanOutcome> CreateManyAsync(
        IReadOnlyList<int> bookIds, int readerAccountId, DateOnly loanDate, string? actor, CancellationToken cancellationToken = default) =>
        await CreateManyCoreAsync(bookIds, readerAccountId, loanDate, actor, null, null, cancellationToken);

    public async Task<BookLoanOutcome> CreateForHoldAsync(
        int bookId, int readerAccountId, DateOnly loanDate, DateOnly originalDueDate, DateOnly dueDate,
        string? actor, CancellationToken cancellationToken = default)
    {
        var result = await CreateManyCoreAsync([bookId], readerAccountId, loanDate, actor,
            originalDueDate, dueDate, cancellationToken);
        return result.IsSuccess
            ? new(true, Loan: result.Loans.FirstOrDefault())
            : new(false, result.ErrorMessage);
    }

    private async Task<BatchBookLoanOutcome> CreateManyCoreAsync(
        IReadOnlyList<int> bookIds, int readerAccountId, DateOnly loanDate, string? actor,
        DateOnly? explicitOriginalDueDate, DateOnly? explicitDueDate, CancellationToken cancellationToken)
    {
        if (bookIds == null || bookIds.Count == 0)
            return new(false, "Vui lòng chọn ít nhất một cuốn sách.", []);

        var reader = await db.ReaderAccounts
            .Include(r => r.LibraryCard)
                .ThenInclude(card => card!.LibraryCardType)
            .FirstOrDefaultAsync(r => r.Id == readerAccountId, cancellationToken);
        if (reader == null) return new(false, "Không tìm thấy bạn đọc.", []);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var checkDate = loanDate != default ? loanDate : today;

        var isCardLocked = reader.IsLocked
            || (reader.LibraryCard != null && string.Equals(reader.LibraryCard.Status, "Bị khoá", StringComparison.OrdinalIgnoreCase))
            || (reader.LibraryCard != null && reader.LibraryCard.IsLocked)
            || reader.Status.Contains("khóa", StringComparison.OrdinalIgnoreCase)
            || reader.Status.Contains("khoá", StringComparison.OrdinalIgnoreCase);

        var isCardExpired = reader.LibraryCard != null && reader.LibraryCard.ExpiresOn < checkDate;

        var readerLoans = await db.BookLoans
            .Where(l => l.ReaderAccountId == readerAccountId)
            .ToListAsync(cancellationToken);

        var hasOverdueLoan = readerLoans.Any(l => !l.IsReturned && l.DueDate < checkDate);

        var totalDebt = reader.TotalDebt;
        var hasUnpaidFee = totalDebt > 0m;

        var maxBooks = reader.LibraryCard?.LibraryCardType?.MaxBooks ?? LibraryCardType.DefaultMaxBooks;
        var currentLoans = readerLoans.Count(l => !l.IsReturned);

        var isLimitReached = currentLoans >= maxBooks;
        var isBatchExceeded = !isLimitReached && (currentLoans + bookIds.Count > maxBooks);

        var errors = new List<string>();

        if (isCardLocked)
        {
            errors.Add("Thẻ bạn đọc đang bị khoá.");
        }

        if (isCardExpired)
        {
            errors.Add("Thẻ bạn đọc đã hết hạn.");
        }

        if (hasOverdueLoan)
        {
            errors.Add("Bạn đọc đang có phiếu mượn quá hạn chưa trả.");
        }

        if (hasUnpaidFee)
        {
            errors.Add($"Bạn còn nợ {FormatVnd(totalDebt)}, không thể mượn sách.");
        }

        if (isLimitReached)
        {
            errors.Add($"Bạn đang mượn {currentLoans}/{maxBooks} sách, không thể mượn thêm.");
        }
        else if (isBatchExceeded)
        {
            errors.Add($"Bạn đang mượn {currentLoans}/{maxBooks} sách, không thể mượn thêm {bookIds.Count} sách vì vượt quá hạn mức ({maxBooks} sách).");
        }

        if (errors.Count > 0)
        {
            var errorMessage = string.Join(" ", errors);
            var operatorName = !string.IsNullOrWhiteSpace(actor) ? actor : "Thủ thư";
            var blockLog = new AuditLog
            {
                OccurredAtUtc = DateTime.UtcNow,
                Actor = operatorName,
                Action = AuditActions.BlockLoan,
                Target = $"Bạn đọc #{reader.Id} {reader.FullName} ({reader.Email}) – Lý do: {errorMessage}",
                IpAddress = "127.0.0.1"
            };
            db.AuditLogs.Add(blockLog);
            await db.SaveChangesAsync(cancellationToken);

            return new(false, errorMessage, []);
        }

        if (!string.Equals(reader.Status, "Đang hoạt động", StringComparison.OrdinalIgnoreCase))
        {
            var operatorName = !string.IsNullOrWhiteSpace(actor) ? actor : "Thủ thư";
            var blockLog = new AuditLog
            {
                OccurredAtUtc = DateTime.UtcNow,
                Actor = operatorName,
                Action = AuditActions.BlockLoan,
                Target = $"Bạn đọc #{reader.Id} {reader.FullName} ({reader.Email}) – Lý do: Bạn đọc chưa ở trạng thái hoạt động.",
                IpAddress = "127.0.0.1"
            };
            db.AuditLogs.Add(blockLog);
            await db.SaveChangesAsync(cancellationToken);

            return new(false, "Bạn đọc chưa ở trạng thái hoạt động.", []);
        }

        var books = new List<Book>();
        foreach (var bookId in bookIds)
        {
            var book = await db.Books.FindAsync([bookId], cancellationToken);
            if (book == null) return new(false, "Không tìm thấy sách.", []);
            books.Add(book);
        }

        var originalDueDate = explicitOriginalDueDate ?? loanDate.AddDays(DefaultLoanDays);
        DateOnly dueDate;
        if (explicitDueDate is { } calculatedDueDate)
        {
            dueDate = calculatedDueDate;
        }
        else
        {
            try { dueDate = await AdjustDueDateAsync(originalDueDate, cancellationToken); }
            catch (InvalidOperationException exception) { return new(false, exception.Message, []); }
        }

        var createdLoans = new List<BookLoan>();
        foreach (var book in books)
        {
            var loan = new BookLoan
            {
                BookId = book.Id,
                ReaderAccountId = readerAccountId,
                LoanDate = loanDate,
                OriginalDueDate = originalDueDate,
                DueDate = dueDate,
                CreatedAtUtc = DateTime.UtcNow,
                Book = book,
                ReaderAccount = reader
            };
            db.BookLoans.Add(loan);
            createdLoans.Add(loan);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new(true, Loans: createdLoans);
    }

    public async Task<RenewBookLoanOutcome> RenewAsync(long loanId, DateOnly today, CancellationToken cancellationToken = default)
    {
        var loan = await db.BookLoans.Include(item => item.ReaderAccount)
            .ThenInclude(reader => reader!.LibraryCard)
            .ThenInclude(card => card!.LibraryCardType)
            .FirstOrDefaultAsync(item => item.Id == loanId, cancellationToken);
        if (loan == null) return new(false, "Không tìm thấy phiếu mượn.");
        if (loan.DueDate < today) return new(false, "Phiếu mượn đã quá hạn.");
        var cardType = loan.ReaderAccount?.LibraryCard?.LibraryCardType;
        if (cardType == null) return new(false, "Bạn đọc chưa có loại thẻ hợp lệ.");
        if (loan.RenewalCount >= cardType.MaxRenewals)
            return new(false, "Bạn đã sử dụng hết số lần gia hạn cho phép của loại thẻ.");

        var nowUtc = DateTime.UtcNow;
        var hasActiveHold = await db.BookHolds.AnyAsync(hold => hold.BookId == loan.BookId &&
            (hold.Status == BookHoldStatus.Waiting ||
             (HoldPickupService.WaitingPickupStatuses.Contains(hold.Status) &&
              (hold.PickupDeadlineUtc == null || hold.PickupDeadlineUtc > nowUtc))), cancellationToken);
        if (hasActiveHold)
            return new(false, "Sách đang có người đặt giữ, không thể gia hạn", ReasonCode: "BOOK_HAS_ACTIVE_HOLD");

        var hasOtherOverdueLoan = await db.BookLoans.AnyAsync(other =>
            other.ReaderAccountId == loan.ReaderAccountId && other.Id != loan.Id && other.DueDate < today,
            cancellationToken);
        if (hasOtherOverdueLoan)
            return new(false,
                "Không thể gia hạn do bạn đang có phiếu mượn khác quá hạn", ReasonCode: "OTHER_OVERDUE_LOAN");

        var hasOutstandingBalance = loan.ReaderAccount!.OutstandingBalance > 0;
        if (hasOutstandingBalance)
            return new(false,
                "Không thể gia hạn do bạn đang có khoản phí chưa thanh toán", ReasonCode: "UNPAID_FEE");

        var oldDueDate = loan.DueDate;
        DateOnly newDueDate;
        try { newDueDate = await AdjustDueDateAsync(oldDueDate.AddDays(DefaultRenewalDays), cancellationToken); }
        catch (InvalidOperationException exception) { return new(false, exception.Message); }

        // Conditional update prevents two simultaneous requests from consuming the same final renewal.
        if (db.Database.IsRelational())
        {
            var updated = await db.BookLoans.Where(item => item.Id == loanId
                    && item.DueDate == oldDueDate && item.RenewalCount == loan.RenewalCount
                    && item.RenewalCount < cardType.MaxRenewals)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.DueDate, newDueDate)
                    .SetProperty(item => item.RenewalCount, item => item.RenewalCount + 1), cancellationToken);
            if (updated == 0) return new(false, "Phiếu mượn vừa được cập nhật hoặc đã hết lượt gia hạn. Vui lòng tải lại trang.");
        }
        else
        {
            loan.DueDate = newDueDate;
            loan.RenewalCount++;
            await db.SaveChangesAsync(cancellationToken);
            return new(true, Loan: loan, OldDueDate: oldDueDate);
        }
        loan.DueDate = newDueDate;
        loan.RenewalCount++;
        return new(true, Loan: loan, OldDueDate: oldDueDate);
    }

    public async Task<RenewBookLoanOutcome> RenewForReaderAsync(long loanId, int readerAccountId, DateOnly today, CancellationToken cancellationToken = default)
    {
        if (!await db.BookLoans.AnyAsync(item => item.Id == loanId && item.ReaderAccountId == readerAccountId, cancellationToken))
            return new(false, "Không tìm thấy phiếu mượn của bạn đọc đang đăng nhập.");
        return await RenewAsync(loanId, today, cancellationToken);
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

    public static string FormatVnd(decimal amount)
    {
        var culture = new System.Globalization.CultureInfo("vi-VN");
        return amount % 1 == 0
            ? $"{amount.ToString("#,##0", culture)} VND"
            : $"{amount.ToString("#,##0.##", culture)} VND";
    }

    public async Task<BookLoanOutcome> OverrideCreateAsync(
        int bookId, int readerAccountId, DateOnly loanDate, string actor, string bypassReason, CancellationToken cancellationToken = default)
    {
        var result = await OverrideCreateManyAsync([bookId], readerAccountId, loanDate, actor, bypassReason, cancellationToken);
        if (!result.IsSuccess)
        {
            return new(false, result.ErrorMessage);
        }
        return new(true, Loan: result.Loans.FirstOrDefault());
    }

    public async Task<BatchBookLoanOutcome> OverrideCreateManyAsync(
        IReadOnlyList<int> bookIds, int readerAccountId, DateOnly loanDate, string actor, string bypassReason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bypassReason))
        {
            return new(false, "Vui lòng nhập lý do bỏ qua.", []);
        }

        if (bookIds == null || bookIds.Count == 0)
        {
            return new(false, "Vui lòng chọn sách.", []);
        }

        var reader = await db.ReaderAccounts
            .Include(item => item.LibraryCard)
                .ThenInclude(card => card!.LibraryCardType)
            .SingleOrDefaultAsync(item => item.Id == readerAccountId, cancellationToken);

        if (reader is null)
        {
            return new(false, "Không tìm thấy bạn đọc.", []);
        }

        var checkDate = loanDate;
        var isCardLocked = reader.IsLocked
            || (reader.LibraryCard != null && string.Equals(reader.LibraryCard.Status, "Bị khoá", StringComparison.OrdinalIgnoreCase))
            || (reader.LibraryCard != null && reader.LibraryCard.IsLocked)
            || reader.Status.Contains("khóa", StringComparison.OrdinalIgnoreCase)
            || reader.Status.Contains("khoá", StringComparison.OrdinalIgnoreCase);

        var isCardExpired = reader.LibraryCard != null && reader.LibraryCard.ExpiresOn < checkDate;

        var readerLoans = await db.BookLoans
            .Where(l => l.ReaderAccountId == readerAccountId)
            .ToListAsync(cancellationToken);

        var hasOverdueLoan = readerLoans.Any(l => !l.IsReturned && l.DueDate < checkDate);

        var totalDebt = reader.TotalDebt;
        var hasUnpaidFee = totalDebt > 0m;

        var maxBooks = reader.LibraryCard?.LibraryCardType?.MaxBooks ?? LibraryCardType.DefaultMaxBooks;
        var currentLoans = readerLoans.Count(l => !l.IsReturned);

        var isLimitReached = currentLoans >= maxBooks;
        var isBatchExceeded = !isLimitReached && (currentLoans + bookIds.Count > maxBooks);

        var errors = new List<string>();

        if (isCardLocked)
        {
            errors.Add("Thẻ bạn đọc đang bị khoá.");
        }

        if (isCardExpired)
        {
            errors.Add("Thẻ bạn đọc đã hết hạn.");
        }

        if (hasOverdueLoan)
        {
            errors.Add("Bạn đọc đang có phiếu mượn quá hạn chưa trả.");
        }

        if (hasUnpaidFee)
        {
            errors.Add($"Bạn còn nợ {FormatVnd(totalDebt)}, không thể mượn sách.");
        }

        if (isLimitReached)
        {
            errors.Add($"Bạn đang mượn {currentLoans}/{maxBooks} sách, không thể mượn thêm.");
        }
        else if (isBatchExceeded)
        {
            errors.Add($"Bạn đang mượn {currentLoans}/{maxBooks} sách, không thể mượn thêm {bookIds.Count} sách vì vượt quá hạn mức ({maxBooks} sách).");
        }

        if (!string.Equals(reader.Status, "Đang hoạt động", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Bạn đọc chưa ở trạng thái hoạt động.");
        }

        var initialReason = errors.Count > 0 ? string.Join(" ", errors) : "Không có vi phạm";
        var operatorName = !string.IsNullOrWhiteSpace(actor) ? actor : "Quản lý thư viện";

        var overrideLog = new AuditLog
        {
            OccurredAtUtc = DateTime.UtcNow,
            Actor = operatorName,
            Action = AuditActions.OverrideBlockLoan,
            Target = $"Bạn đọc #{reader.Id} {reader.FullName} ({reader.Email}) – Lý do chặn: {initialReason} – Lý do bỏ qua: {bypassReason.Trim()}",
            IpAddress = "127.0.0.1"
        };
        db.AuditLogs.Add(overrideLog);
        await db.SaveChangesAsync(cancellationToken);

        var books = new List<Book>();
        foreach (var bookId in bookIds)
        {
            var book = await db.Books.FindAsync([bookId], cancellationToken);
            if (book == null) return new(false, "Không tìm thấy sách.", []);
            books.Add(book);
        }

        var originalDueDate = loanDate.AddDays(DefaultLoanDays);
        DateOnly dueDate;
        try { dueDate = await AdjustDueDateAsync(originalDueDate, cancellationToken); }
        catch (InvalidOperationException exception) { return new(false, exception.Message, []); }

        var createdLoans = new List<BookLoan>();
        foreach (var book in books)
        {
            var loan = new BookLoan
            {
                BookId = book.Id,
                ReaderAccountId = readerAccountId,
                LoanDate = loanDate,
                OriginalDueDate = originalDueDate,
                DueDate = dueDate,
                CreatedAtUtc = DateTime.UtcNow,
                Book = book,
                ReaderAccount = reader
            };
            db.BookLoans.Add(loan);
            createdLoans.Add(loan);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new(true, Loans: createdLoans);
    }

    public async Task<IReadOnlyList<BlockedLoanLogEntry>> GetBlockedLoanLogsAsync(
        int? readerAccountId = null, CancellationToken cancellationToken = default)
    {
        var logs = await db.AuditLogs.AsNoTracking()
            .Where(l => l.Action == AuditActions.BlockLoan || l.Action == AuditActions.OverrideBlockLoan)
            .OrderByDescending(l => l.OccurredAtUtc)
            .ToListAsync(cancellationToken);

        var result = new List<BlockedLoanLogEntry>();
        foreach (var log in logs)
        {
            var entry = ToBlockedLoanLogEntry(log);
            if (readerAccountId == null || entry.ReaderAccountId == readerAccountId.Value)
            {
                result.Add(entry);
            }
        }
        return result;
    }

    public static BlockedLoanLogEntry ToBlockedLoanLogEntry(AuditLog log)
    {
        var entry = new BlockedLoanLogEntry
        {
            Id = log.Id,
            OccurredAtUtc = log.OccurredAtUtc,
            Operator = log.Actor,
            Target = log.Target
        };

        if (log.Action == AuditActions.OverrideBlockLoan)
        {
            entry.Status = "Đã bỏ qua";
            const string blockedPrefix = " – Lý do chặn: ";
            const string bypassPrefix = " – Lý do bỏ qua: ";
            var blockIdx = log.Target.IndexOf(blockedPrefix, StringComparison.Ordinal);
            var bypassIdx = log.Target.IndexOf(bypassPrefix, StringComparison.Ordinal);

            if (blockIdx >= 0 && bypassIdx > blockIdx)
            {
                var readerPart = log.Target[..blockIdx].Trim();
                entry.Reason = log.Target[(blockIdx + blockedPrefix.Length)..bypassIdx].Trim();
                entry.BypassReason = log.Target[(bypassIdx + bypassPrefix.Length)..].Trim();
                ParseReaderPart(readerPart, entry);
            }
            else
            {
                entry.Reason = log.Target;
            }
        }
        else
        {
            entry.Status = "Bị chặn";
            const string reasonPrefix = " – Lý do: ";
            var reasonIdx = log.Target.IndexOf(reasonPrefix, StringComparison.Ordinal);
            if (reasonIdx >= 0)
            {
                entry.Reason = log.Target[(reasonIdx + reasonPrefix.Length)..].Trim();
                var readerPart = log.Target[..reasonIdx].Trim();
                ParseReaderPart(readerPart, entry);
            }
            else
            {
                entry.Reason = log.Target;
            }
        }

        return entry;
    }

    private static void ParseReaderPart(string readerPart, BlockedLoanLogEntry entry)
    {
        var match = System.Text.RegularExpressions.Regex.Match(readerPart, @"Bạn đọc #(\d+)\s+([^(]+)(?:\(([^)]+)\))?");
        if (match.Success)
        {
            if (int.TryParse(match.Groups[1].Value, out var id))
                entry.ReaderAccountId = id;
            entry.ReaderName = match.Groups[2].Value.Trim();
            if (match.Groups.Count > 3)
                entry.ReaderEmail = match.Groups[3].Value.Trim();
        }
        else
        {
            entry.ReaderName = readerPart;
        }
    }
}
