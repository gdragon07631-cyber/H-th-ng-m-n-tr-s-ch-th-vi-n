using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Services;

public sealed class ShelfService(ApplicationDbContext db) : IShelfService
{
    public async Task<IReadOnlyList<Shelf>> GetAllAsync(int? warehouseId = null, CancellationToken cancellationToken = default)
    {
        var query = db.Shelves.Include(s => s.Warehouse).AsQueryable();
        if (warehouseId.HasValue)
        {
            query = query.Where(s => s.WarehouseId == warehouseId.Value);
        }
        return await query.OrderBy(s => s.Warehouse!.Name).ThenBy(s => s.Code).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Shelf>> GetActiveAsync(int? warehouseId = null, CancellationToken cancellationToken = default)
    {
        var query = db.Shelves
            .AsNoTracking()
            .Include(s => s.Warehouse)
            .Where(s => s.Status == ShelfStatus.Active && s.Warehouse!.Status == WarehouseStatus.Active);

        if (warehouseId.HasValue)
        {
            query = query.Where(s => s.WarehouseId == warehouseId.Value);
        }
        return await query.OrderBy(s => s.Warehouse!.Name).ThenBy(s => s.Code).ToListAsync(cancellationToken);
    }

    public async Task<Shelf?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        await db.Shelves.Include(s => s.Warehouse).FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<ShelfOutcome> CreateAsync(
        int warehouseId,
        string code,
        string name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        var warehouse = await db.Warehouses.FindAsync([warehouseId], cancellationToken);
        if (warehouse == null) return new(false, "Kho trực thuộc không tồn tại.");

        var normalizedCode = code?.Trim() ?? string.Empty;
        var normalizedName = name?.Trim() ?? string.Empty;

        if (normalizedCode.Length == 0) return new(false, "Vui lòng nhập mã kệ.");
        if (normalizedCode.Length > 50) return new(false, "Mã kệ không được vượt quá 50 ký tự.");
        if (normalizedName.Length == 0) return new(false, "Vui lòng nhập tên kệ.");
        if (normalizedName.Length > 150) return new(false, "Tên kệ không được vượt quá 150 ký tự.");
        if (description?.Length > 500) return new(false, "Mô tả không được vượt quá 500 ký tự.");

        // Kiểm tra mã kệ DUY NHẤT trong phạm vi MỘT kho
        if (await db.Shelves.AnyAsync(s => s.WarehouseId == warehouseId && s.Code == normalizedCode, cancellationToken))
        {
            return new(false, $"Mã kệ '{normalizedCode}' đã tồn tại trong kho này.");
        }

        var shelf = new Shelf
        {
            WarehouseId = warehouseId,
            Code = normalizedCode,
            Name = normalizedName,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Status = ShelfStatus.Active,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.Shelves.Add(shelf);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return new(false, $"Mã kệ '{normalizedCode}' đã tồn tại trong kho này.");
        }

        shelf.Warehouse = warehouse;
        return new(true, Shelf: shelf);
    }

    public async Task<ShelfOutcome> UpdateAsync(
        int id,
        int warehouseId,
        string code,
        string name,
        string? description,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var shelf = await db.Shelves.Include(s => s.Warehouse).FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (shelf == null) return new(false, "Không tìm thấy kệ sách.");

        var warehouse = await db.Warehouses.FindAsync([warehouseId], cancellationToken);
        if (warehouse == null) return new(false, "Kho trực thuộc không tồn tại.");

        var normalizedCode = code?.Trim() ?? string.Empty;
        var normalizedName = name?.Trim() ?? string.Empty;

        if (normalizedCode.Length == 0) return new(false, "Vui lòng nhập mã kệ.");
        if (normalizedCode.Length > 50) return new(false, "Mã kệ không được vượt quá 50 ký tự.");
        if (normalizedName.Length == 0) return new(false, "Vui lòng nhập tên kệ.");
        if (normalizedName.Length > 150) return new(false, "Tên kệ không được vượt quá 150 ký tự.");
        if (description?.Length > 500) return new(false, "Mô tả không được vượt quá 500 ký tự.");

        // Kiểm tra mã kệ DUY NHẤT trong cùng kho khi cập nhật
        if (await db.Shelves.AnyAsync(s => s.WarehouseId == warehouseId && s.Code == normalizedCode && s.Id != id, cancellationToken))
        {
            return new(false, $"Mã kệ '{normalizedCode}' đã tồn tại trong kho này.");
        }

        shelf.WarehouseId = warehouseId;
        shelf.Code = normalizedCode;
        shelf.Name = normalizedName;
        shelf.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (!string.IsNullOrWhiteSpace(status))
        {
            shelf.Status = status.Trim();
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return new(false, $"Mã kệ '{normalizedCode}' đã tồn tại trong kho này.");
        }

        shelf.Warehouse = warehouse;
        return new(true, Shelf: shelf);
    }

    public async Task<ShelfOutcome> DeactivateAsync(int id, CancellationToken cancellationToken = default)
    {
        var shelf = await db.Shelves.FindAsync([id], cancellationToken);
        if (shelf == null) return new(false, "Không tìm thấy kệ sách.");

        shelf.Status = ShelfStatus.Inactive;
        await db.SaveChangesAsync(cancellationToken);
        return new(true, Shelf: shelf);
    }

    public async Task<ShelfOutcome> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var shelf = await db.Shelves.FindAsync([id], cancellationToken);
        if (shelf == null) return new(false, "Không tìm thấy kệ sách.");

        db.Shelves.Remove(shelf);
        await db.SaveChangesAsync(cancellationToken);
        return new(true, Shelf: shelf);
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
