using Project.Models;

namespace Project.Services;

public sealed record LoanContactHistoryOutcome(bool IsSuccess, string? ErrorMessage = null, LoanContactHistoryItem? Item = null);

public interface ILoanContactHistoryService
{
    Task<LoanContactHistoryOutcome> AddAsync(long loanId, string? note, AdminAccount staff, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LoanContactHistoryItem>> GetByLoanIdAsync(long loanId, CancellationToken cancellationToken = default);
}
