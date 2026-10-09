using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Services;

public sealed class BookHoldQueueService(ApplicationDbContext db) : IBookHoldQueueService
{
    // Includes the current statuses and names used by earlier hold-pickup data.
    private static readonly string[] ActiveStatuses = BookHoldStatus.ActiveStatuses;

    public async Task<IReadOnlyList<BookHoldQueueItemViewModel>> GetActiveQueueForBookAsync(
        int bookId, CancellationToken cancellationToken = default)
    {
        var holds = await db.BookHolds
            .AsNoTracking()
            .Where(hold => hold.BookId == bookId && ActiveStatuses.Contains(hold.Status))
            .OrderBy(hold => hold.HeldAtUtc)
            .ThenBy(hold => hold.Id)
            .Select(hold => new
            {
                hold.Id,
                LoanId = db.BookLoans.Where(loan => loan.SourceBookHoldId == hold.Id ||
                    (hold.Status == BookHoldStatus.ConvertedToLoan && loan.SourceBookHoldId == null &&
                     hold.BookCopyId != null && loan.BookCopyId == hold.BookCopyId &&
                     loan.ReaderAccountId == hold.ReaderAccountId && loan.BookId == hold.BookId))
                    .OrderByDescending(loan => loan.CreatedAtUtc).Select(loan => (long?)loan.Id).FirstOrDefault(),
                hold.ReaderAccountId,
                ReaderName = hold.ReaderAccount!.FullName,
                hold.HeldAtUtc,
                hold.Status,
                hold.BookCopyId,
                CopyBarcode = hold.BookCopy == null ? null : hold.BookCopy.CopyCode,
                hold.PickupDeadlineUtc,
                hold.CancellationReason,
                hold.CancelledAtUtc
            })
            .ToListAsync(cancellationToken);

        return holds.Select((hold, index) => new BookHoldQueueItemViewModel
        {
            HoldId = hold.Id,
            LoanId = hold.LoanId,
            Position = index + 1,
            ReaderAccountId = hold.ReaderAccountId,
            ReaderName = hold.ReaderName,
            HeldAtUtc = hold.HeldAtUtc,
            Status = hold.Status,
            BookCopyId = hold.BookCopyId,
            CopyBarcode = hold.CopyBarcode,
            PickupDeadlineUtc = hold.PickupDeadlineUtc,
            CancellationReason = hold.CancellationReason,
            CancelledAtUtc = hold.CancelledAtUtc,
            CanBeCancelledByStaff = CanBeCancelledByStaff(hold.Status)
        }).ToList();
    }

    public async Task<IReadOnlyList<BookHoldQueueItemViewModel>> GetQueueForBookAsync(
        int bookId, string filter, CancellationToken cancellationToken = default)
    {
        if (!BookHoldQueueFilter.TryNormalize(filter, out var normalizedFilter))
            throw new ArgumentException("Bộ lọc trạng thái không hợp lệ.", nameof(filter));

        // Number positions before filtering so a librarian retains the original queue position.
        var allHolds = await db.BookHolds
            .AsNoTracking()
            .Where(hold => hold.BookId == bookId)
            .OrderBy(hold => hold.HeldAtUtc)
            .ThenBy(hold => hold.Id)
            .Select(hold => new
            {
                hold.Id,
                LoanId = db.BookLoans.Where(loan => loan.SourceBookHoldId == hold.Id ||
                    (hold.Status == BookHoldStatus.ConvertedToLoan && loan.SourceBookHoldId == null &&
                     hold.BookCopyId != null && loan.BookCopyId == hold.BookCopyId &&
                     loan.ReaderAccountId == hold.ReaderAccountId && loan.BookId == hold.BookId))
                    .OrderByDescending(loan => loan.CreatedAtUtc).Select(loan => (long?)loan.Id).FirstOrDefault(),
                hold.ReaderAccountId,
                ReaderName = hold.ReaderAccount!.FullName,
                hold.HeldAtUtc,
                hold.Status,
                hold.BookCopyId,
                CopyBarcode = hold.BookCopy == null ? null : hold.BookCopy.CopyCode,
                hold.PickupDeadlineUtc,
                hold.CancellationReason,
                hold.CancelledAtUtc
            })
            .ToListAsync(cancellationToken);

        return allHolds.Select((hold, index) => new BookHoldQueueItemViewModel
            {
                HoldId = hold.Id,
                LoanId = hold.LoanId,
                Position = index + 1,
                ReaderAccountId = hold.ReaderAccountId,
                ReaderName = hold.ReaderName,
                HeldAtUtc = hold.HeldAtUtc,
                Status = hold.Status,
                BookCopyId = hold.BookCopyId,
                CopyBarcode = hold.CopyBarcode,
                PickupDeadlineUtc = hold.PickupDeadlineUtc,
                CancellationReason = hold.CancellationReason,
                CancelledAtUtc = hold.CancelledAtUtc,
                CanBeCancelledByStaff = CanBeCancelledByStaff(hold.Status)
            })
            .Where(item => MatchesFilter(item.Status, normalizedFilter))
            .ToList();
    }

    private static bool MatchesFilter(string status, string filter) => filter switch
    {
        BookHoldQueueFilter.All => true,
        BookHoldQueueFilter.Queued => status == BookHoldStatus.Waiting,
        BookHoldQueueFilter.WaitingPickup => HoldPickupService.WaitingPickupStatuses.Contains(status),
        BookHoldQueueFilter.ConvertedToLoan => status == BookHoldStatus.ConvertedToLoan,
        BookHoldQueueFilter.Cancelled => status == BookHoldStatus.Cancelled,
        _ => false
    };

    private static bool CanBeCancelledByStaff(string status) =>
        status == BookHoldStatus.Waiting || HoldPickupService.WaitingPickupStatuses.Contains(status);
}
