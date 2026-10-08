using Project.Models;

namespace Project.Services;

public interface IStaffHoldCancellationService
{
    Task<StaffHoldCancellationOutcome> CancelAsync(long holdId, string? reason, int cancelledByAdminAccountId,
        CancellationToken cancellationToken = default);
}

public enum StaffHoldCancellationResult { Success, NotFound, MissingReason, AlreadyCancelled, ConvertedToLoan, InvalidStatus }

public sealed record StaffHoldCancellationOutcome(StaffHoldCancellationResult Result, string Message, BookHold? Hold = null)
{
    public bool IsSuccess => Result == StaffHoldCancellationResult.Success;
}
