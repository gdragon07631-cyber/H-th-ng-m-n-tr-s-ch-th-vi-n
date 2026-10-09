using Project.Models;

namespace Project.Services;

public interface IBookLoanDetailsService
{
    Task<BookLoanDetailsViewModel?> GetHoldConversionDetailsAsync(long loanId, CancellationToken cancellationToken = default);
}
