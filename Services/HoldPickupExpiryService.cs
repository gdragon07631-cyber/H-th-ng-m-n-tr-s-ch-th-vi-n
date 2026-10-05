using System.Data;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Services;

public sealed class HoldPickupExpiryService(
    ApplicationDbContext db,
    IBookHoldFulfillmentService holdFulfillmentService,
    ILogger<HoldPickupExpiryService> logger) : IHoldPickupExpiryService
{
    public const string ExpiredReason = "Quá hạn nhận sách – hệ thống tự hủy";
    private const string SystemActor = "System: hold pickup expiry";

    public async Task<int> ExpireOverdueAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var overdueIds = await db.BookHolds
            .Where(hold => HoldPickupService.WaitingPickupStatuses.Contains(hold.Status) &&
                hold.PickupDeadlineUtc != null && hold.PickupDeadlineUtc < nowUtc)
            .OrderBy(hold => hold.PickupDeadlineUtc)
            .Select(hold => hold.Id)
            .ToListAsync(cancellationToken);

        var expired = 0;
        foreach (var holdId in overdueIds)
        {
            // Mỗi đơn một giao dịch: một đơn lỗi không chặn các đơn khác.
            try
            {
                if (await ExpireOneAsync(holdId, nowUtc, cancellationToken)) expired++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Không thể tự hủy đơn đặt giữ quá hạn nhận #{HoldId}.", holdId);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
        if (expired > 0) logger.LogInformation("Đã tự hủy {Count} đơn đặt giữ quá hạn nhận.", expired);
        return expired;
    }

    private async Task<bool> ExpireOneAsync(long holdId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var hold = await db.BookHolds.Include(item => item.BookCopy)
            .SingleOrDefaultAsync(item => item.Id == holdId, cancellationToken);
        // Kiểm tra lại trong giao dịch: đơn có thể vừa được nhận sách hoặc bị hủy.
        if (hold == null || !HoldPickupService.WaitingPickupStatuses.Contains(hold.Status) ||
            hold.PickupDeadlineUtc == null || hold.PickupDeadlineUtc >= nowUtc)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return false;
        }

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
                Reason = $"Released after hold #{hold.Id} passed its pickup deadline",
                ChangedBy = SystemActor,
                ChangedAtUtc = nowUtc
            });
        }
        hold.BookCopyId = null;
        hold.PickupDeadlineUtc = null;
        hold.Status = BookHoldStatus.Cancelled;
        hold.CancellationReason = ExpiredReason;
        hold.CancelledAtUtc = nowUtc;
        var bookTitle = await db.Books.Where(book => book.Id == hold.BookId).Select(book => book.Title).SingleOrDefaultAsync(cancellationToken);
        var readerEmail = await db.ReaderAccounts.Where(reader => reader.Id == hold.ReaderAccountId)
            .Select(reader => reader.Email).SingleOrDefaultAsync(cancellationToken);
        db.AuditLogs.Add(AuditLogService.Create(
            "Hệ thống",
            AuditActions.CancelHold,
            $"Đơn đặt giữ #{hold.Id} \"{bookTitle}\" (sách #{hold.BookId}) của bạn đọc #{hold.ReaderAccountId} ({readerEmail}) – " +
                "tự hủy vì quá hạn nhận sách" + (releasedCopy != null ? $", trả bản sao {releasedCopy.CopyCode} về kệ" : string.Empty),
            AuditLogService.UnknownIpAddress));
        await db.SaveChangesAsync(cancellationToken);

        // Bản sao vừa trả về kệ được giữ ngay cho người kế tiếp trong hàng đợi (nếu có).
        if (releasedCopy != null) await holdFulfillmentService.FulfillNextAsync(hold.BookId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
