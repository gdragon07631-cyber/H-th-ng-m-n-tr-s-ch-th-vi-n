using Project.Models;

namespace Project.Services;

public sealed record CategoryOutcome(bool IsSuccess, string? ErrorMessage = null, Category? Category = null, bool HasLinkedBooks = false);

public interface ICategoryService
{
    Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Category>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<CategoryOutcome> CreateAsync(string name, int? parentId, CancellationToken cancellationToken = default);
    Task<CategoryOutcome> UpdateAsync(int id, string name, int? parentId, bool parentIdSpecified = true, CancellationToken cancellationToken = default);
    Task<CategoryOutcome> DeactivateAsync(int id, CancellationToken cancellationToken = default);
    Task<CategoryOutcome> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
