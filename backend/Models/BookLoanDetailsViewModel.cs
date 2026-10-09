namespace Project.Models;

public sealed class BookLoanDetailsViewModel
{
    public long LoanId { get; init; }
    public long HoldId { get; init; }
    public string CopyBarcode { get; init; } = string.Empty;
    public string BookTitle { get; init; } = string.Empty;
    public string LibraryCardCode { get; init; } = string.Empty;
    public string ReaderName { get; init; } = string.Empty;
    public string ReaderEmail { get; init; } = string.Empty;
    public string ReaderPhone { get; init; } = string.Empty;
    public DateOnly LoanDate { get; init; }
    public DateOnly DueDate { get; init; }
    public string CreatedByName { get; init; } = string.Empty;
    public string CreatedByEmail { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public string LoanStatus { get; init; } = string.Empty;
    public string TransactionStatus { get; init; } = string.Empty;
}
