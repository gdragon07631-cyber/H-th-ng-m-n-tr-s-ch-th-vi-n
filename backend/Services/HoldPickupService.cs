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
                .ThenInclude(r => r!.LibraryCard)
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
            LibraryCardCode = h.ReaderAccount?.LibraryCard?.CardCode ?? string.Empty,
            ReaderEmail = h.ReaderAccount?.Email ?? string.Empty,
            ReaderPhone = h.ReaderAccount?.PhoneNumber ?? string.Empty,
            PickupDeadlineUtc = h.PickupDeadlineUtc,
            HeldAtUtc = h.HeldAtUtc,
            Status = h.Status,
            CurrentShelf = h.BookCopy?.Shelf?.Name,
            CurrentWarehouse = h.BookCopy?.Shelf?.Warehouse?.Name
        }).ToList();
    }

    public async Task<HoldPickupItemViewModel?> GetHoldAsync(long holdId, CancellationToken cancellationToken = default)
    {
        var hold = await db.BookHolds.AsNoTracking()
            .Include(h => h.ReaderAccount).ThenInclude(r => r!.LibraryCard)
            .Include(h => h.Book)
            .Include(h => h.BookCopy).ThenInclude(c => c!.Shelf).ThenInclude(s => s!.Warehouse)
            .FirstOrDefaultAsync(h => h.Id == holdId, cancellationToken);
        if (hold == null) return null;

        return new HoldPickupItemViewModel
        {
            HoldId = hold.Id, BookId = hold.BookId, BookTitle = hold.Book?.Title ?? "—", Isbn = hold.Book?.Isbn,
            BookCopyId = hold.BookCopyId, CopyBarcode = hold.BookCopy?.CopyCode ?? "—",
            ReaderAccountId = hold.ReaderAccountId, ReaderName = hold.ReaderAccount?.FullName ?? "—",
            LibraryCardCode = hold.ReaderAccount?.LibraryCard?.CardCode ?? string.Empty,
            ReaderEmail = hold.ReaderAccount?.Email ?? string.Empty, ReaderPhone = hold.ReaderAccount?.PhoneNumber ?? string.Empty,
            PickupDeadlineUtc = hold.PickupDeadlineUtc, HeldAtUtc = hold.HeldAtUtc, Status = hold.Status,
            CancellationReason = hold.CancellationReason,
            CurrentShelf = hold.BookCopy?.Shelf?.Name, CurrentWarehouse = hold.BookCopy?.Shelf?.Warehouse?.Name
        };
    }
}

