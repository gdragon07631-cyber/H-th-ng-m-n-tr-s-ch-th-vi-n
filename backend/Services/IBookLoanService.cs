using Project.Models;

namespace Project.Services;

public sealed record BookLoanOutcome(bool IsSuccess, string? ErrorMessage = null, BookLoan? Loan = null);
public sealed record RenewBookLoanOutcome(
    bool IsSuccess, string? ErrorMessage = null, BookLoan? Loan = null,
    DateOnly? OldDueDate = null, string? ReasonCode = null);

public interface IBookLoanService
{
    Task<IReadOnlyList<BookLoan>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<BookLoanOutcome> CreateAsync(int bookId, int readerAccountId, DateOnly loanDate, CancellationToken cancellationToken = default);
    Task<RenewBookLoanOutcome> RenewAsync(long loanId, DateOnly today, CancellationToken cancellationToken = default);
    Task<DateOnly> AdjustDueDateAsync(DateOnly proposedDate, CancellationToken cancellationToken = default);
<<<<<<< Updated upstream
=======
    Task<IReadOnlyList<BlockedLoanLogEntry>> GetBlockedLoanLogsAsync(int? readerAccountId = null, CancellationToken cancellationToken = default);
    Task<BookLoanOutcome> OverrideCreateAsync(int bookId, int readerAccountId, DateOnly loanDate, string actor, string bypassReason, CancellationToken cancellationToken = default);
    Task<BatchBookLoanOutcome> OverrideCreateManyAsync(IReadOnlyList<int> bookIds, int readerAccountId, DateOnly loanDate, string actor, string bypassReason, CancellationToken cancellationToken = default);
>>>>>>> Stashed changes
}
