using System.Data;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Services;

public sealed class HoldPickupConfirmationService(ApplicationDbContext db, IBookLoanService loanService)
    : IHoldPickupConfirmationService
{
    public async Task<HoldPickupConfirmationResult> ConfirmAsync(
        long holdId, string libraryCardCode, string actor, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var hold = await db.BookHolds
                .Include(item => item.ReaderAccount).ThenInclude(reader => reader!.LibraryCard)
                .Include(item => item.BookCopy)
                .FirstOrDefaultAsync(item => item.Id == holdId, cancellationToken);
            if (hold == null)
                return await RejectAsync(transaction, "Không tìm thấy đơn đặt giữ.", cancellationToken);

            if (!HoldPickupService.WaitingPickupStatuses.Contains(hold.Status))
            {
                var message = hold.Status == BookHoldStatus.ConvertedToLoan
                    ? "Đơn đặt giữ này đã được chuyển thành phiếu mượn."
                    : "Đơn đặt giữ không còn ở trạng thái chờ nhận.";
                return await RejectAsync(transaction, message, cancellationToken);
            }

            if (hold.BookCopyId == null || hold.BookCopy == null)
                return await RejectAsync(transaction, "Đơn đặt giữ chưa được gán bản sao sách.", cancellationToken);

            if (hold.ReaderAccount?.LibraryCard == null ||
                !string.Equals(hold.ReaderAccount.LibraryCard.CardCode, libraryCardCode?.Trim(), StringComparison.Ordinal))
                return await RejectAsync(transaction, "Mã thẻ bạn đọc không khớp với đơn đặt giữ.", cancellationToken);

            if (hold.BookCopy.BookId != hold.BookId || hold.BookCopy.Status != BookCopyStatus.OnHold)
                return await RejectAsync(transaction, "Bản sao được giữ không còn ở trạng thái hợp lệ để cho mượn.", cancellationToken);

            var now = DateTime.UtcNow;
            var loanDate = DateOnly.FromDateTime(DateTime.Today);
            var loanResult = await loanService.CreateAsync(hold.BookId, hold.ReaderAccountId, loanDate, actor, cancellationToken);
            if (!loanResult.IsSuccess || loanResult.Loan == null)
                return await RejectAsync(transaction, loanResult.ErrorMessage ?? "Không thể tạo phiếu mượn.", cancellationToken);

            var copy = hold.BookCopy;
            copy.Status = BookCopyStatus.OnLoan;
            copy.StatusReason = $"Issued for book hold #{hold.Id}";
            loanResult.Loan.BookCopyId = copy.Id;
            hold.Status = BookHoldStatus.ConvertedToLoan;

            db.BookCopyStatusHistories.Add(new BookCopyStatusHistory
            {
                BookCopyId = copy.Id,
                FromStatus = BookCopyStatus.OnHold,
                ToStatus = BookCopyStatus.OnLoan,
                Reason = $"Issued for book hold #{hold.Id}",
                ChangedBy = string.IsNullOrWhiteSpace(actor) ? "Thủ thư" : actor,
                ChangedAtUtc = now
            });

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(true, $"Đã xác nhận đơn đặt giữ và tạo phiếu mượn #{loanResult.Loan.Id}.", loanResult.Loan.Id);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<HoldPickupConfirmationResult> RejectAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        string message, CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        return new(false, message);
    }
}
