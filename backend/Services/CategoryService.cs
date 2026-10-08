using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Services;

public sealed class CategoryService(ApplicationDbContext db) : ICategoryService
{
    public async Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Categories.Include(c => c.Children).Include(c => c.Parent)
            .OrderBy(c => c.Name).ThenBy(c => c.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Category>> GetActiveAsync(CancellationToken cancellationToken = default) =>
        await db.Categories.AsNoTracking().Include(c => c.Parent)
            .Where(c => c.Status == CategoryStatus.Active &&
                (c.ParentId == null || c.Parent!.Status == CategoryStatus.Active))
            .OrderBy(c => c.Name).ThenBy(c => c.Id).ToListAsync(cancellationToken);

    public async Task<CategoryOutcome> CreateAsync(string name, int? parentId, CancellationToken cancellationToken = default)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length == 0) return new(false, "Vui lòng nhập tên thể loại.");
        if (normalized.Length > 150) return new(false, "Tên thể loại không được vượt quá 150 ký tự.");
        if (await db.Categories.AnyAsync(c => c.Name == normalized, cancellationToken)) return new(false, "Tên thể loại đã tồn tại.");
        var parentError = await ValidateParentAsync(parentId, null, cancellationToken);
        if (parentError != null) return new(false, parentError);

        var category = new Category { Name = normalized, ParentId = parentId, Status = CategoryStatus.Active, CreatedAtUtc = DateTime.UtcNow };
        db.Categories.Add(category);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return new(false, "Tên thể loại đã tồn tại."); }
        return new(true, Category: category);
    }

    public async Task<CategoryOutcome> UpdateAsync(int id, string name, int? parentId, bool parentIdSpecified = true, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.Include(c => c.Children).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category == null) return new(false, "Không tìm thấy thể loại.");
        if (!parentIdSpecified) parentId = category.ParentId;
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length == 0) return new(false, "Vui lòng nhập tên thể loại.");
        if (normalized.Length > 150) return new(false, "Tên thể loại không được vượt quá 150 ký tự.");
        if (await db.Categories.AnyAsync(c => c.Id != id && c.Name == normalized, cancellationToken)) return new(false, "Tên thể loại đã tồn tại.");
        var parentError = await ValidateParentAsync(parentId, category, cancellationToken);
        if (parentError != null) return new(false, parentError);
        category.Name = normalized;
        category.ParentId = parentId;
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return new(false, "Tên thể loại đã tồn tại."); }
        return new(true, Category: category);
    }

    public async Task<CategoryOutcome> DeactivateAsync(int id, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.FindAsync([id], cancellationToken);
        if (category == null) return new(false, "Không tìm thấy thể loại.");
        category.Status = CategoryStatus.Inactive;
        await db.SaveChangesAsync(cancellationToken);
        return new(true, Category: category);
    }

    public async Task<CategoryOutcome> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.FindAsync([id], cancellationToken);
        if (category == null) return new(false, "Không tìm thấy thể loại.");
        if (await db.Books.AnyAsync(b => b.CategoryId == id, cancellationToken))
            return new(false, "Không thể xóa thể loại này vì đã có sách liên kết.", HasLinkedBooks: true);
        db.Categories.Remove(category);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { return new(false, "Không thể xóa thể loại vì dữ liệu đang được sử dụng.", HasLinkedBooks: true); }
        return new(true, Category: category);
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };

    private async Task<string?> ValidateParentAsync(int? parentId, Category? category, CancellationToken cancellationToken)
    {
        if (parentId == null)
        {
            return null;
        }
        if (category?.Id == parentId)
        {
            return "Thể loại không thể làm danh mục cha của chính nó.";
        }
        if (category is { Children.Count: > 0 })
        {
            return "Không thể tạo Thể loại cấp 3. Hệ thống chỉ hỗ trợ tối đa 2 cấp.";
        }

        var parent = await db.Categories.FirstOrDefaultAsync(c => c.Id == parentId.Value, cancellationToken);
        if (parent == null) return "Không tìm thấy Thể loại cha được chọn.";
        if (parent.Status != CategoryStatus.Active) return "Không thể chọn Thể loại ngừng sử dụng làm danh mục cha.";
        if (parent.ParentId != null)
            return "Không thể tạo Thể loại cấp 3. Hệ thống chỉ hỗ trợ tối đa 2 cấp.";
        return null;
    }
}
