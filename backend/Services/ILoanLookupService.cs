using Project.Models;

namespace Project.Services;

public interface ILoanLookupService
{
    /// <summary>
    /// Tra phiếu mượn theo một mã duy nhất (mã thẻ, mã vạch bản sao hoặc mã phiếu). Trả null khi mã rỗng.
    /// </summary>
    Task<LoanLookupPage?> SearchAsync(string? code, int page, CancellationToken cancellationToken = default);

    /// <summary>
    /// Như <see cref="SearchAsync(string?, int, CancellationToken)"/> nhưng thu hẹp theo khoảng ngày mượn và trạng thái.
    /// Bộ lọc không hợp lệ gây <see cref="ArgumentException"/>. Cài đặt mặc định chỉ hỗ trợ bộ lọc rỗng.
    /// </summary>
    Task<LoanLookupPage?> SearchAsync(string? code, int page, LoanLookupFilter filter, CancellationToken cancellationToken = default) =>
        filter.IsEmpty
            ? SearchAsync(code, page, cancellationToken)
            : throw new NotSupportedException("Dịch vụ tra cứu này không hỗ trợ bộ lọc.");
}
