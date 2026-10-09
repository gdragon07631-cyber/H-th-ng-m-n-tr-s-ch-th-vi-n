using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Services;

public sealed class LoanContactHistoryService(ApplicationDbContext db) : ILoanContactHistoryService
{
    public async Task<LoanContactHistoryOutcome> AddAsync(
        long loanId, string? note, AdminAccount staff, CancellationToken cancellationToken = default)
    {
        var trimmedNote = note?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedNote)) return new(false, "Vui lòng nhập nội dung ghi chú liên hệ.");
        if (trimmedNote.Length > 1000) return new(false, "Ghi chú liên hệ không được vượt quá 1000 ký tự.");
        if (!await db.BookLoans.AnyAsync(loan => loan.Id == loanId, cancellationToken))
            return new(false, "Không tìm thấy phiếu mượn.");

        var history = new LoanContactHistory
        {
            BookLoanId = loanId,
            Note = trimmedNote,
            CreatedAtUtc = DateTime.UtcNow,
            ContactedByAdminAccountId = staff.Id,
            ContactedBy = string.IsNullOrWhiteSpace(staff.FullName) ? staff.Email : staff.FullName
        };
        db.LoanContactHistories.Add(history);
        await db.SaveChangesAsync(cancellationToken);
        return new(true, Item: ToItem(history));
    }

    public async Task<IReadOnlyList<LoanContactHistoryItem>> GetByLoanIdAsync(long loanId, CancellationToken cancellationToken = default) =>
        await db.LoanContactHistories.AsNoTracking().Where(history => history.BookLoanId == loanId)
            .OrderByDescending(history => history.CreatedAtUtc).ThenByDescending(history => history.Id)
            .Select(history => new LoanContactHistoryItem(history.Id, history.BookLoanId, history.CreatedAtUtc,
                history.Note, history.ContactedByAdminAccountId, history.ContactedBy))
            .ToListAsync(cancellationToken);

    private static LoanContactHistoryItem ToItem(LoanContactHistory history) => new(
        history.Id, history.BookLoanId, history.CreatedAtUtc, history.Note,
        history.ContactedByAdminAccountId, history.ContactedBy);
}
