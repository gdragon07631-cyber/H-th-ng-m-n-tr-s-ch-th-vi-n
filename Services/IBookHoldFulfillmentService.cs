namespace Project.Services;

public sealed record BookHoldFulfillmentResult(
    bool IsAssigned,
    long? HoldId = null,
    long? BookCopyId = null,
    string? CopyCode = null,
    DateTime? PickupDeadlineUtc = null,
    string? ErrorMessage = null);

public interface IBookHoldFulfillmentService
{
    /// <summary>Assigns one ready copy to the first active hold, if that hold is still waiting.</summary>
    Task<BookHoldFulfillmentResult> FulfillNextAsync(int bookId, CancellationToken cancellationToken = default);
}
