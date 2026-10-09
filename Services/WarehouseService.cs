using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Services;

public sealed class WarehouseService(ApplicationDbContext db) : IWarehouseService
{
    public async Task<IReadOnlyList<Warehouse>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Warehouses
            .Include(w => w.Shelves)
            .OrderBy(w => w.Code)
            .ThenBy(w => w.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Warehouse>> GetActiveAsync(CancellationToken cancellationToken = default) =>
        await db.Warehouses
            .AsNoTracking()
            .Include(w => w.Shelves)
            .Where(w => w.Status == WarehouseStatus.Active)
            .OrderBy(w => w.Code)
            .ThenBy(w => w.Name)
            .ToListAsync(cancellationToken);

    public async Task<Warehouse?> GetByIdAsync(int id, bool includeShelves = false, CancellationToken cancellationToken = default)
    {
        var query = db.Warehouses.AsQueryable();
        if (includeShelves)
        {
            query = query.Include(w => w.Shelves.OrderBy(s => s.Code));
        }
        return await query.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
    }

    public async Task<WarehouseOutcome> CreateAsync(
        string code,
        string name,
        string? address,
        string? description,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = code?.Trim() ?? string.Empty;
        var normalizedName = name?.Trim() ?? string.Empty;

        if (normalizedCode.Length == 0) return new(false, "Vui lòng nhập mã kho.");
        if (normalizedCode.Length > 50) return new(false, "Mã kho không được vượt quá 50 ký tự.");
        if (normalizedName.Length == 0) return new(false, "Vui lòng nhập tên kho.");
        if (normalizedName.Length > 150) return new(false, "Tên kho không được vượt quá 150 ký tự.");
        if (address?.Length > 500) return new(false, "Địa chỉ không được vượt quá 500 ký tự.");
        if (description?.Length > 500) return new(false, "Mô tả không được vượt quá 500 ký tự.");

        if (await db.Warehouses.AnyAsync(w => w.Code == normalizedCode, cancellationToken))
        {
            return new(false, $"Mã kho '{normalizedCode}' đã tồn tại trong hệ thống.");
        }

        var warehouse = new Warehouse
        {
            Code = normalizedCode,
            Name = normalizedName,
            Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Status = WarehouseStatus.Active,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.Warehouses.Add(warehouse);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return new(false, $"Mã kho '{normalizedCode}' đã tồn tại trong hệ thống.");
        }

        return new(true, Warehouse: warehouse);
    }

    public async Task<WarehouseOutcome> UpdateAsync(
        int id,
        string code,
        string name,
        string? address,
        string? description,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var warehouse = await db.Warehouses.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (warehouse == null) return new(false, "Không tìm thấy kho.");

        var normalizedCode = code?.Trim() ?? string.Empty;
        var normalizedName = name?.Trim() ?? string.Empty;

        if (normalizedCode.Length == 0) return new(false, "Vui lòng nhập mã kho.");
        if (normalizedCode.Length > 50) return new(false, "Mã kho không được vượt quá 50 ký tự.");
        if (normalizedName.Length == 0) return new(false, "Vui lòng nhập tên kho.");
        if (normalizedName.Length > 150) return new(false, "Tên kho không được vượt quá 150 ký tự.");
        if (address?.Length > 500) return new(false, "Địa chỉ không được vượt quá 500 ký tự.");
        if (description?.Length > 500) return new(false, "Mô tả không được vượt quá 500 ký tự.");

        if (await db.Warehouses.AnyAsync(w => w.Id != id && w.Code == normalizedCode, cancellationToken))
        {
            return new(false, $"Mã kho '{normalizedCode}' đã tồn tại trong hệ thống.");
        }

        warehouse.Code = normalizedCode;
        warehouse.Name = normalizedName;
        warehouse.Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        warehouse.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (!string.IsNullOrWhiteSpace(status))
        {
            warehouse.Status = status.Trim();
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return new(false, $"Mã kho '{normalizedCode}' đã tồn tại trong hệ thống.");
        }

        return new(true, Warehouse: warehouse);
    }

    public async Task<WarehouseOutcome> DeactivateAsync(int id, CancellationToken cancellationToken = default)
    {
        var warehouse = await db.Warehouses.FindAsync([id], cancellationToken);
        if (warehouse == null) return new(false, "Không tìm thấy kho.");

        warehouse.Status = WarehouseStatus.Inactive;
        await db.SaveChangesAsync(cancellationToken);
        return new(true, Warehouse: warehouse);
    }

    public async Task<WarehouseOutcome> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var warehouse = await db.Warehouses.FindAsync([id], cancellationToken);
        if (warehouse == null) return new(false, "Không tìm thấy kho.");

        if (await db.Shelves.AnyAsync(s => s.WarehouseId == id, cancellationToken))
        {
            return new(false, "Không thể xóa kho vì còn kệ sách trực thuộc.", HasLinkedShelves: true);
        }

        db.Warehouses.Remove(warehouse);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return new(false, "Không thể xóa kho vì dữ liệu đang được sử dụng.", HasLinkedShelves: true);
        }

        return new(true, Warehouse: warehouse);
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
