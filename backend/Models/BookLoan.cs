using System.ComponentModel.DataAnnotations;

namespace Project.Models;

public sealed class BookLoan
{
    public long Id { get; set; }
    public int BookId { get; set; }
    public Book? Book { get; set; }
    public int ReaderAccountId { get; set; }
    public ReaderAccount? ReaderAccount { get; set; }
    public DateOnly LoanDate { get; set; }
    public DateOnly OriginalDueDate { get; set; }
    public DateOnly DueDate { get; set; }
    public int RenewalCount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class CreateBookLoanViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn sách.")]
    public int BookId { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn bạn đọc.")]
    public int ReaderAccountId { get; set; }
    [Required(ErrorMessage = "Vui lòng chọn ngày mượn.")]
    public DateOnly? LoanDate { get; set; }
<<<<<<< Updated upstream
=======

    public string? BypassReason { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var hasSingle = BookId > 0;
        var hasMultiple = BookIds != null && BookIds.Count > 0 && BookIds.All(id => id > 0);
        if (!hasSingle && !hasMultiple)
        {
            yield return new ValidationResult("Vui lòng chọn sách.", [nameof(BookId)]);
        }
    }
}

public sealed class CreateBatchBookLoanViewModel
{
    public List<int> BookIds { get; set; } = [];

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn bạn đọc.")]
    public int ReaderAccountId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn ngày mượn.")]
    public DateOnly? LoanDate { get; set; }

    public string? BypassReason { get; set; }
}

public sealed class OverrideBookLoanViewModel : IValidatableObject
{
    public int BookId { get; set; }
    public List<int>? BookIds { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn bạn đọc.")]
    public int ReaderAccountId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn ngày mượn.")]
    public DateOnly? LoanDate { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập lý do bỏ qua."), MaxLength(500)]
    public string BypassReason { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var hasSingle = BookId > 0;
        var hasMultiple = BookIds != null && BookIds.Count > 0 && BookIds.All(id => id > 0);
        if (!hasSingle && !hasMultiple)
        {
            yield return new ValidationResult("Vui lòng chọn sách.", [nameof(BookId)]);
        }

        if (string.IsNullOrWhiteSpace(BypassReason))
        {
            yield return new ValidationResult("Vui lòng nhập lý do bỏ qua.", [nameof(BypassReason)]);
        }
    }
}

public sealed class BlockedLoanLogEntry
{
    public long Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string Operator { get; set; } = string.Empty;
    public int ReaderAccountId { get; set; }
    public string ReaderName { get; set; } = string.Empty;
    public string ReaderEmail { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? BypassReason { get; set; }
    public string Status { get; set; } = "Bị chặn";
    public bool IsOverridden => Status == "Đã bỏ qua";
    public string Target { get; set; } = string.Empty;
>>>>>>> Stashed changes
}

public sealed class LoanIndexViewModel
{
    public IReadOnlyList<BookLoan> Loans { get; set; } = [];
    public IReadOnlyList<Book> Books { get; set; } = [];
    public IReadOnlyList<ReaderAccount> Readers { get; set; } = [];
    public CreateBookLoanViewModel NewLoan { get; set; } = new();
    public bool CanOverride { get; set; }
}
