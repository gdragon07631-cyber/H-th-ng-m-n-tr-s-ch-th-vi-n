using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Services;

/// <summary>Staff-only cancellation. The reason rule is enforced here, not only in the UI.</summary>
public sealed class StaffHoldCancellationService(ApplicationDbContext db) : IStaffHoldCancellationService
{
    public async Task<StaffHoldCancellationOutcome> CancelAsync(long holdId, string? reason, int cancelledByAdminAccountId,
        CancellationToken cancellationToken = default)
    {
        var trimmedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedReason))
            return new(StaffHoldCancellationResult.MissingReason, "Vui lòng nhập lý do hủy yêu cầu đặt giữ.");

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
        if (hold.BookCopy != null) hold.BookCopy.Status = BookCopyStatus.Available;
        hold.BookCopyId = null;
        hold.PickupDeadlineUtc = null;
        hold.Status = BookHoldStatus.Cancelled;
        hold.CancellationReason = trimmedReason;
        hold.CancelledAtUtc = DateTime.UtcNow;
        hold.CancelledByAdminAccountId = cancelledByAdminAccountId;
        await db.SaveChangesAsync(cancellationToken);
        return new(StaffHoldCancellationResult.Success, "Hủy yêu cầu đặt giữ thành công.", hold);
    }
}
