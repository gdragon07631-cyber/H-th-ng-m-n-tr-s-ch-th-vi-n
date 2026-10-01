using Project.Models;

namespace Project.Services;

public enum BookCopyUpdateStatus
{
    Success,
    NotFound,
    InvalidShelf,
    InvalidCondition,
    InvalidStatus,
    OnActiveLoan,
    StatusManagedByHold,
    ReasonRequired,
    DuplicateCode
}

public sealed record BookCopyUpdateResult(BookCopyUpdateStatus Status, string? ErrorMessage = null, BookCopy? Copy = null)
{
    public bool IsSuccess => Status == BookCopyUpdateStatus.Success;
}

public interface IBookCopyService
{
    Task<BookCopyIndexViewModel?> GetBookCopiesAsync(int bookId, CancellationToken cancellationToken = default);
    Task<BookCopyEditViewModel?> GetForEditAsync(long copyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Warehouse>> GetActiveWarehousesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Shelf>> GetActiveShelvesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BookCopyStatusHistory>> GetHistoryAsync(long copyId, CancellationToken cancellationToken = default);

    Task<BookCopyUpdateResult> AddAsync(int bookId, NewBookCopyViewModel model, CancellationToken cancellationToken = default);

    /// <summary>Sửa kho, kệ, tình trạng vật lý, ghi chú và trạng thái; mã vạch giữ nguyên. Đổi trạng thái được ghi lịch sử.</summary>
    Task<BookCopyUpdateResult> UpdateAsync(long copyId, BookCopyEditViewModel model, string changedBy, CancellationToken cancellationToken = default);

    /// <summary>Số bản sao rảnh (trạng thái "Sẵn sàng") và tổng số bản sao của một đầu sách.</summary>
    Task<(int Available, int Total)> CountCopiesAsync(int bookId, CancellationToken cancellationToken = default);
}
