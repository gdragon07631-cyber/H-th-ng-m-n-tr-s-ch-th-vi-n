using Project.Models;

namespace Project.Services;

public sealed record ShelfOutcome(
    bool IsSuccess,
    string? ErrorMessage = null,
    Shelf? Shelf = null,
    bool HasLinkedBookCopies = false);

public interface IShelfService
{
    Task<IReadOnlyList<Shelf>> GetAllAsync(int? warehouseId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Shelf>> GetActiveAsync(int? warehouseId = null, CancellationToken cancellationToken = default);
    Task<Shelf?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ShelfOutcome> CreateAsync(int warehouseId, string code, string name, string? description, CancellationToken cancellationToken = default);
    Task<ShelfOutcome> UpdateAsync(int id, int warehouseId, string code, string name, string? description, string? status = null, CancellationToken cancellationToken = default);
    Task<ShelfOutcome> DeactivateAsync(int id, CancellationToken cancellationToken = default);
    Task<ShelfOutcome> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
