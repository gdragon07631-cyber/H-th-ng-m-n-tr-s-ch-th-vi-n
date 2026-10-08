namespace Project.Models;

public static class BookHoldStatus
{
    public const string Waiting = "Đang chờ";
    public const string Available = "Đã có sách";
    public const string Cancelled = "Đã hủy";
    public const string ConvertedToLoan = "Đã chuyển thành phiếu mượn";

    public static readonly string[] WaitingPickupStatuses =
    [
        Available,
        "Chờ nhận",
        "Sẵn sàng nhận",
        "WaitingPickup"
    ];

    public static readonly string[] ActiveStatuses = [Waiting, .. WaitingPickupStatuses];
}

public sealed class BookHold
{
    public long Id { get; set; }
    public int ReaderAccountId { get; set; }
    public ReaderAccount? ReaderAccount { get; set; }
    public int BookId { get; set; }
    public Book? Book { get; set; }
    public DateTime HeldAtUtc { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = BookHoldStatus.Waiting;
    /// <summary>Hạn cuối nhận sách (UTC); chỉ có khi đơn ở trạng thái "Đã có sách".</summary>
    public DateTime? PickupDeadlineUtc { get; set; }
    /// <summary>Bản sao đang được giữ cho đơn này (nếu có).</summary>
    public long? BookCopyId { get; set; }
    public BookCopy? BookCopy { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(1000)]
    public string? CancellationReason { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public int? CancelledByAdminAccountId { get; set; }
}
