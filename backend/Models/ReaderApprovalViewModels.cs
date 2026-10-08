using System.ComponentModel.DataAnnotations;

namespace Project.Models;

public sealed class ReaderApprovalIndexViewModel
{
    public IReadOnlyList<ReaderAccount> PendingReaders { get; set; } = [];
    public IReadOnlyList<LibraryCardType> CardTypes { get; set; } = [];
}

public sealed class ApproveReaderViewModel
{
    [Required]
    public int ReaderAccountId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn loại thẻ.")]
    public int LibraryCardTypeId { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateOnly IssuedOn { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Required]
    [DataType(DataType.Date)]
    public DateOnly ExpiresOn { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddYears(1));
}

public sealed class RejectReaderViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập lý do từ chối.")]
    [MaxLength(1000, ErrorMessage = "Lý do từ chối không được vượt quá 1000 ký tự.")]
    public string RejectionReason { get; set; } = string.Empty;
}
