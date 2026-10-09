using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Services;

public sealed class BookLoanDetailsService(ApplicationDbContext db) : IBookLoanDetailsService
{
    public async Task<BookLoanDetailsViewModel?> GetHoldConversionDetailsAsync(long loanId, CancellationToken cancellationToken = default)
    {
        var loan = await db.BookLoans.AsNoTracking()
            .Where(item => item.Id == loanId)
            .Include(item => item.BookCopy).ThenInclude(copy => copy!.Book)
            .Include(item => item.Book)
            .Include(item => item.ReaderAccount).ThenInclude(reader => reader!.LibraryCard)
            .Include(item => item.CreatedByAdminAccount)
            .Include(item => item.SourceBookHold)
            .SingleOrDefaultAsync(cancellationToken);

        if (loan == null) return null;

        var hold = loan.SourceBookHold;
        if (hold == null && loan.BookCopyId is long copyId)
        {
            // L1 loans created before this trace migration retained the exact copy on both rows.
            hold = await db.BookHolds.AsNoTracking()
                .Where(item => item.Status == BookHoldStatus.ConvertedToLoan && item.BookCopyId == copyId &&
                    item.ReaderAccountId == loan.ReaderAccountId && item.BookId == loan.BookId && item.HeldAtUtc <= loan.CreatedAtUtc)
                .OrderByDescending(item => item.HeldAtUtc).ThenByDescending(item => item.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
        if (hold == null) return null;

        var creatorName = loan.CreatedByAdminAccount?.FullName;
        var creatorEmail = loan.CreatedByAdminAccount?.Email;
        if (loan.CreatedByAdminAccount == null && loan.BookCopyId is long historyCopyId)
        {
            var historyActor = await db.BookCopyStatusHistories.AsNoTracking()
                .Where(item => item.BookCopyId == historyCopyId && item.ToStatus == BookCopyStatus.OnLoan &&
                    item.Reason == $"Issued for book hold #{hold.Id}" && item.ChangedAtUtc <= loan.CreatedAtUtc)
                .OrderByDescending(item => item.ChangedAtUtc)
                .Select(item => item.ChangedBy)
                .FirstOrDefaultAsync(cancellationToken);
            creatorName = historyActor;
        }

        return new BookLoanDetailsViewModel
        {
            LoanId = loan.Id,
            HoldId = hold.Id,
            CopyBarcode = loan.BookCopy?.CopyCode ?? string.Empty,
            BookTitle = loan.BookCopy?.Book?.Title ?? loan.Book?.Title ?? string.Empty,
            LibraryCardCode = loan.ReaderAccount?.LibraryCard?.CardCode ?? string.Empty,
            ReaderName = loan.ReaderAccount?.FullName ?? string.Empty,
            ReaderEmail = loan.ReaderAccount?.Email ?? string.Empty,
            ReaderPhone = loan.ReaderAccount?.PhoneNumber ?? string.Empty,
            LoanDate = loan.LoanDate,
            DueDate = loan.DueDate,
            CreatedByName = creatorName ?? string.Empty,
            CreatedByEmail = creatorEmail ?? string.Empty,
            CreatedAtUtc = loan.CreatedAtUtc,
            LoanStatus = loan.IsReturned ? "Đã trả" : "Đang mượn",
            TransactionStatus = hold.Status == BookHoldStatus.ConvertedToLoan
                ? "Đã chuyển thành phiếu mượn"
                : hold.Status
        };
    }
}
