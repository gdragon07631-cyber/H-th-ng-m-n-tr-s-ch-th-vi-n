using Microsoft.EntityFrameworkCore;
using System.Data;
using Project.Data;
using Project.Models;

namespace Project.Services;

/// <summary>Staff-only cancellation. The reason rule is enforced here, not only in the UI.</summary>
public sealed class StaffHoldCancellationService(ApplicationDbContext db,
    IBookHoldFulfillmentService? holdFulfillmentService = null) : IStaffHoldCancellationService
{
    private readonly IBookHoldFulfillmentService holdFulfillmentService = holdFulfillmentService ??
        new BookHoldFulfillmentService(db, new BookLoanService(db, new WorkingScheduleService(db)));

    public async Task<StaffHoldCancellationOutcome> CancelAsync(long holdId, string? reason, int cancelledByAdminAccountId,
        CancellationToken cancellationToken = default)
    {
        var trimmedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedReason))
            return new(StaffHoldCancellationResult.MissingReason, "Vui lòng nhập lý do hủy yêu cầu đặt giữ.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var hold = await db.BookHolds.Include(item => item.BookCopy).SingleOrDefaultAsync(item => item.Id == holdId, cancellationToken);
        if (hold == null) return new(StaffHoldCancellationResult.NotFound, "Không tìm thấy yêu cầu đặt giữ.");
        if (hold.Status == BookHoldStatus.Cancelled)
            return new(StaffHoldCancellationResult.AlreadyCancelled, "Yêu cầu đặt giữ này đã được hủy.");
        if (hold.Status == BookHoldStatus.ConvertedToLoan)
            return new(StaffHoldCancellationResult.ConvertedToLoan, "Không thể hủy vì yêu cầu đã chuyển thành phiếu mượn.");

        var canCancel = hold.Status == BookHoldStatus.Waiting || HoldPickupService.WaitingPickupStatuses.Contains(hold.Status);
        if (!canCancel)
            return new(StaffHoldCancellationResult.InvalidStatus, "Yêu cầu đặt giữ không ở trạng thái có thể hủy.");

        // A reserved copy must be released when a waiting-pickup hold is cancelled.
        var releasedCopy = hold.BookCopy;
        if (releasedCopy != null)
        {
            var previousStatus = releasedCopy.Status;
            releasedCopy.Status = BookCopyStatus.Available;
            releasedCopy.StatusReason = null;
            db.BookCopyStatusHistories.Add(new BookCopyStatusHistory
            {
                BookCopyId = releasedCopy.Id,
                FromStatus = previousStatus,
                ToStatus = BookCopyStatus.Available,
                Reason = $"Released after hold #{hold.Id} was cancelled",
                ChangedBy = $"StaffAccount#{cancelledByAdminAccountId}",
                ChangedAtUtc = DateTime.UtcNow
            });
        }
        hold.BookCopyId = null;
        hold.PickupDeadlineUtc = null;
        hold.Status = BookHoldStatus.Cancelled;
        hold.CancellationReason = trimmedReason;
        hold.CancelledAtUtc = DateTime.UtcNow;
        hold.CancelledByAdminAccountId = cancelledByAdminAccountId;
        await db.SaveChangesAsync(cancellationToken);
        if (releasedCopy != null)
        {
            // A released copy may immediately serve the next waiting request.
            await holdFulfillmentService.FulfillNextAsync(hold.BookId, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new(StaffHoldCancellationResult.Success, "Hủy yêu cầu đặt giữ thành công.", hold);
    }
}
