using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Services;

public sealed class HoldPickupService(ApplicationDbContext db) : IHoldPickupService
{
    public static readonly string[] WaitingPickupStatuses = BookHoldStatus.WaitingPickupStatuses;

    public async Task<IReadOnlyList<HoldPickupItemViewModel>> GetWaitingPickupHoldsAsync(CancellationToken cancellationToken = default)
    {
        var holds = await db.BookHolds
            .AsNoTracking()
            .Include(h => h.ReaderAccount)
            .Include(h => h.Book)
            .Include(h => h.BookCopy)
                .ThenInclude(c => c!.Shelf)
                    .ThenInclude(s => s!.Warehouse)
            .Where(h => WaitingPickupStatuses.Contains(h.Status)
                        && h.BookCopyId != null
                        && h.BookCopy != null)
            .OrderBy(h => h.PickupDeadlineUtc)
            .ThenBy(h => h.Id)
            .ToListAsync(cancellationToken);

        return holds.Select(h => new HoldPickupItemViewModel
        {
            HoldId = h.Id,
            BookId = h.BookId,
            BookTitle = h.Book?.Title ?? "—",
            Isbn = h.Book?.Isbn,
            BookCopyId = h.BookCopyId!.Value,
            CopyBarcode = h.BookCopy?.CopyCode ?? "—",
            ReaderAccountId = h.ReaderAccountId,
            ReaderName = h.ReaderAccount?.FullName ?? "—",
            ReaderEmail = h.ReaderAccount?.Email ?? string.Empty,
            ReaderPhone = h.ReaderAccount?.PhoneNumber ?? string.Empty,
            PickupDeadlineUtc = h.PickupDeadlineUtc,
            HeldAtUtc = h.HeldAtUtc,
            Status = h.Status,
            CurrentShelf = h.BookCopy?.Shelf?.Name,
            CurrentWarehouse = h.BookCopy?.Shelf?.Warehouse?.Name
        }).ToList();
    }
}

