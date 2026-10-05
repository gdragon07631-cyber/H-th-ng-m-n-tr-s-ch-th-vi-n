using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Services;

public sealed class BookCopyService(ApplicationDbContext dbContext) : IBookCopyService
{
    public async Task<BookCopyIndexViewModel?> GetBookCopiesAsync(int bookId, CancellationToken cancellationToken = default)
    {
        var title = await dbContext.Books.AsNoTracking()
            .Where(book => book.Id == bookId).Select(book => book.Title)
            .SingleOrDefaultAsync(cancellationToken);
        if (title is null) return null;

        return new BookCopyIndexViewModel
        {
            BookId = bookId,
            BookTitle = title,
            Copies = await dbContext.BookCopies.AsNoTracking()
                .Include(copy => copy.Shelf).ThenInclude(shelf => shelf!.Warehouse)
                .Where(copy => copy.BookId == bookId)
                .OrderBy(copy => copy.CopyCode)
                .ToListAsync(cancellationToken),
            Shelves = await GetActiveShelvesAsync(cancellationToken)
        };
    }

    public async Task<BookCopyEditViewModel?> GetForEditAsync(long copyId, CancellationToken cancellationToken = default)
    {
        var copy = await dbContext.BookCopies.AsNoTracking()
            .Include(item => item.Book)
            .Include(item => item.Shelf)
            .SingleOrDefaultAsync(item => item.Id == copyId, cancellationToken);
        if (copy is null) return null;

        return new BookCopyEditViewModel
        {
            Id = copy.Id,
            CopyCode = copy.CopyCode,
            BookId = copy.BookId,
            BookTitle = copy.Book?.Title ?? string.Empty,
            CurrentStatus = copy.Status,
            WarehouseId = copy.Shelf?.WarehouseId ?? 0,
            ShelfId = copy.ShelfId,
            PhysicalCondition = copy.PhysicalCondition,
            Status = copy.Status,
            Note = copy.Note
        };
    }

    public async Task<IReadOnlyList<Warehouse>> GetActiveWarehousesAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Warehouses.AsNoTracking()
            .Where(warehouse => warehouse.Status == WarehouseStatus.Active)
            .OrderBy(warehouse => warehouse.Code)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Shelf>> GetActiveShelvesAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Shelves.AsNoTracking()
            .Include(shelf => shelf.Warehouse)
            .Where(shelf => shelf.Status == ShelfStatus.Active && shelf.Warehouse!.Status == WarehouseStatus.Active)
            .OrderBy(shelf => shelf.Warehouse!.Code).ThenBy(shelf => shelf.Code)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<BookCopyStatusHistory>> GetHistoryAsync(long copyId, CancellationToken cancellationToken = default) =>
        await dbContext.BookCopyStatusHistories.AsNoTracking()
            .Where(history => history.BookCopyId == copyId)
            .OrderByDescending(history => history.ChangedAtUtc).ThenByDescending(history => history.Id)
            .ToListAsync(cancellationToken);

    public async Task<BookCopyUpdateResult> AddAsync(int bookId, NewBookCopyViewModel model, CancellationToken cancellationToken = default)
    {
        if (!await dbContext.Books.AnyAsync(book => book.Id == bookId, cancellationToken))
            return new(BookCopyUpdateStatus.NotFound, "Không tìm thấy đầu sách.");
        if (!BookCopyCondition.All.Contains(model.PhysicalCondition))
            return new(BookCopyUpdateStatus.InvalidCondition, "Tình trạng vật lý không hợp lệ.");
        if (!await IsActiveShelfAsync(model.ShelfId, warehouseId: null, cancellationToken))
            return new(BookCopyUpdateStatus.InvalidShelf, "Kệ không tồn tại hoặc đã ngừng sử dụng.");

        var code = model.CopyCode.Trim();
        if (await dbContext.BookCopies.AnyAsync(copy => copy.CopyCode == code, cancellationToken))
            return new(BookCopyUpdateStatus.DuplicateCode, $"Mã vạch {code} đã được dùng cho một bản sao khác.");

        var copy = new BookCopy
        {
            BookId = bookId,
            ShelfId = model.ShelfId,
            CopyCode = code,
            Status = BookCopyStatus.Available,
            PhysicalCondition = model.PhysicalCondition,
            Note = string.IsNullOrWhiteSpace(model.Note) ? null : model.Note.Trim()
        };
        dbContext.BookCopies.Add(copy);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(BookCopyUpdateStatus.Success, Copy: copy);
    }

    public async Task<BookCopyUpdateResult> UpdateAsync(long copyId, BookCopyEditViewModel model, string changedBy, CancellationToken cancellationToken = default)
    {
        var copy = await dbContext.BookCopies.SingleOrDefaultAsync(item => item.Id == copyId, cancellationToken);
        if (copy is null) return new(BookCopyUpdateStatus.NotFound, "Không tìm thấy bản sao.");
        if (!BookCopyCondition.All.Contains(model.PhysicalCondition))
            return new(BookCopyUpdateStatus.InvalidCondition, "Tình trạng vật lý không hợp lệ.");
        if (!await IsActiveShelfAsync(model.ShelfId, model.WarehouseId, cancellationToken))
            return new(BookCopyUpdateStatus.InvalidShelf, "Kệ đã chọn không thuộc kho đã chọn hoặc đã ngừng sử dụng.");

        var statusChanged = !string.Equals(copy.Status, model.Status, StringComparison.Ordinal);
        if (statusChanged)
        {
            if (copy.Status == BookCopyStatus.OnLoan)
                return new(BookCopyUpdateStatus.OnActiveLoan,
                    "Không thể chuyển sang Đang sửa chữa: bản sao đang thuộc một phiếu mượn chưa trả.");
            if (copy.Status == BookCopyStatus.OnHold)
                return new(BookCopyUpdateStatus.StatusManagedByHold,
                    "Không thể đổi trạng thái: bản sao đang được giữ cho một đơn đặt giữ.");
            if (!BookCopyStatus.Editable.Contains(model.Status))
                return new(BookCopyUpdateStatus.InvalidStatus, "Trạng thái chỉ được chọn Sẵn sàng hoặc Đang sửa chữa.");
            if (string.IsNullOrWhiteSpace(model.Reason))
                return new(BookCopyUpdateStatus.ReasonRequired, "Vui lòng nhập lý do khi đổi trạng thái bản sao.");

            dbContext.BookCopyStatusHistories.Add(new BookCopyStatusHistory
            {
                BookCopyId = copy.Id,
                FromStatus = copy.Status,
                ToStatus = model.Status,
                Reason = model.Reason.Trim(),
                ChangedBy = changedBy,
                ChangedAtUtc = DateTime.UtcNow
            });
            copy.Status = model.Status;
        }

        // Mã vạch (CopyCode) cố ý không được gán lại.
        copy.ShelfId = model.ShelfId;
        copy.PhysicalCondition = model.PhysicalCondition;
        copy.Note = string.IsNullOrWhiteSpace(model.Note) ? null : model.Note.Trim();
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(BookCopyUpdateStatus.Success, Copy: copy);
    }

    public async Task<(int Available, int Total)> CountCopiesAsync(int bookId, CancellationToken cancellationToken = default)
    {
        var statuses = await dbContext.BookCopies.AsNoTracking()
            .Where(copy => copy.BookId == bookId)
            .Select(copy => copy.Status)
            .ToListAsync(cancellationToken);
        return (statuses.Count(status => status == BookCopyStatus.Available), statuses.Count);
    }

    private Task<bool> IsActiveShelfAsync(int shelfId, int? warehouseId, CancellationToken cancellationToken) =>
        dbContext.Shelves.AnyAsync(shelf =>
            shelf.Id == shelfId &&
            shelf.Status == ShelfStatus.Active &&
            shelf.Warehouse!.Status == WarehouseStatus.Active &&
            (warehouseId == null || shelf.WarehouseId == warehouseId), cancellationToken);
}
