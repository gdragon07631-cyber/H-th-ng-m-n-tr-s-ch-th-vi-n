using Project.Models;

namespace Project.Services;

public interface IBookHoldQueueService
{
    /// <summary>Gets every active hold for one title in its established FIFO order.</summary>
    Task<IReadOnlyList<BookHoldQueueItemViewModel>> GetActiveQueueForBookAsync(
        int bookId, CancellationToken cancellationToken = default);

    /// <summary>Gets a title's holds, optionally filtered by a librarian queue-status filter.</summary>
    Task<IReadOnlyList<BookHoldQueueItemViewModel>> GetQueueForBookAsync(
        int bookId, string filter, CancellationToken cancellationToken = default);
}
