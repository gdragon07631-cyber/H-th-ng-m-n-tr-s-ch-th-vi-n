using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Services;

/// <summary>
/// Tra nhanh phiếu mượn bằng một ô tìm kiếm. Mã được bỏ khoảng trắng đầu/cuối và so khớp chính xác với cả ba loại
/// mã cùng lúc (không đoán loại theo độ dài/tiền tố); một phiếu khớp nhiều điều kiện chỉ xuất hiện một lần vì
/// mọi điều kiện được gộp bằng OR trên cùng bảng BookLoans.
/// </summary>
public sealed class LoanLookupService(ApplicationDbContext db) : ILoanLookupService
{
    public const int PageSize = 20;

    public Task<LoanLookupPage?> SearchAsync(string? code, int page, CancellationToken cancellationToken = default) =>
        SearchAsync(code, page, LoanLookupFilter.None, cancellationToken);

    public async Task<LoanLookupPage?> SearchAsync(
        string? code, int page, LoanLookupFilter filter, CancellationToken cancellationToken = default)
    {
        var filterError = filter.Validate();
        if (filterError is not null) throw new ArgumentException(filterError, nameof(filter));

        var term = code?.Trim();
        if (string.IsNullOrEmpty(term)) return null;

        // Mã phiếu là khóa chính BookLoan.Id; nhận cả dạng "#123" như cách nhật ký hoạt động hiển thị.
        var loanIdText = term.StartsWith('#') ? term[1..] : term;
        long? loanId = long.TryParse(loanIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : null;

        // Mã vạch bản sao (BookCopy.CopyCode) không thể khớp phiếu nào: BookLoan chỉ lưu đầu sách (BookId), chưa lưu
        // bản sao được mượn. Không suy phiếu từ đầu sách của bản sao vì sẽ trả về phiếu không chứa bản sao đó.
        var query = db.BookLoans.AsNoTracking().Where(loan =>
            (loanId != null && loan.Id == loanId) ||
            db.LibraryCards.Any(card => card.ReaderAccountId == loan.ReaderAccountId && card.CardCode == term));
        var codeOnlyQuery = query;

        // Bộ lọc nối bằng AND sau điều kiện mã nên áp dụng cho mọi nhánh khớp mã. LoanDate là cột date (không có
        // giờ, không lưu UTC) nên "≥ đầu ngày Từ" và "< đầu ngày sau Đến" chính là From ≤ LoanDate ≤ To.
        if (filter.From is DateOnly from) query = query.Where(loan => loan.LoanDate >= from);
        if (filter.To is DateOnly to) query = query.Where(loan => loan.LoanDate <= to);
        // Trạng thái đã được kiểm tra thuộc LoanLookupStatus.All; mọi phiếu còn bản ghi đều "Đang mượn" nên lọc theo
        // trạng thái này giữ nguyên tập kết quả.

        var total = await query.CountAsync(cancellationToken);
        var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)PageSize);
        page = totalPages == 0 ? 1 : Math.Clamp(page, 1, totalPages);

        // Mọi phiếu còn bản ghi đều đang mở, nên nhóm "đang mở" là toàn bộ kết quả; trong nhóm sắp theo ngày mượn
        // giảm dần rồi khóa chính giảm dần để phân trang ổn định.
        var items = await query
            .OrderByDescending(loan => loan.LoanDate).ThenByDescending(loan => loan.Id)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(loan => new LoanLookupItem(
                loan.Id,
                db.LibraryCards.Where(card => card.ReaderAccountId == loan.ReaderAccountId)
                    .Select(card => card.CardCode).FirstOrDefault(),
                loan.ReaderAccount!.FullName,
                loan.LoanDate,
                loan.DueDate,
                LoanLookupStatus.Open))
            .ToListAsync(cancellationToken);

        return new LoanLookupPage(term, items, page, PageSize, total)
        {
            Filter = filter,
            EmptyReason = total > 0 ? null : await FindEmptyReasonAsync(codeOnlyQuery, filter, cancellationToken)
        };
    }

    /// <summary>
    /// Chỉ chạy khi không có kết quả. Không có bộ lọc thì chắc chắn là mã không khớp (không cần truy vấn thêm); có bộ
    /// lọc thì kiểm tra tồn tại trên cùng điều kiện mã (cả ba loại mã), chỉ bỏ khoảng ngày và trạng thái.
    /// </summary>
    private static async Task<LoanLookupEmptyReason> FindEmptyReasonAsync(
        IQueryable<BookLoan> codeOnlyQuery, LoanLookupFilter filter, CancellationToken cancellationToken)
    {
        if (filter.IsEmpty) return LoanLookupEmptyReason.NoMatchingCode;
        return await codeOnlyQuery.AnyAsync(cancellationToken)
            ? LoanLookupEmptyReason.FilteredOut
            : LoanLookupEmptyReason.NoMatchingCode;
    }
}
