using Project.Models;

namespace Project.Services;

public sealed record WarehouseOutcome(
    bool IsSuccess,
    string? ErrorMessage = null,
    Warehouse? Warehouse = null,
    bool HasLinkedShelves = false);

public interface IWarehouseService
{
    Task<IReadOnlyList<Warehouse>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Warehouse>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<Warehouse?> GetByIdAsync(int id, bool includeShelves = false, CancellationToken cancellationToken = default);
    Task<WarehouseOutcome> CreateAsync(string code, string name, string? address, string? description, CancellationToken cancellationToken = default);
    Task<WarehouseOutcome> UpdateAsync(int id, string code, string name, string? address, string? description, string? status = null, CancellationToken cancellationToken = default);
    Task<WarehouseOutcome> DeactivateAsync(int id, CancellationToken cancellationToken = default);
    Task<WarehouseOutcome> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
