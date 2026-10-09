using System.ComponentModel.DataAnnotations;

namespace Project.Models;

/// <summary>Một lần liên hệ độc lập với bạn đọc về một phiếu mượn.</summary>
public sealed class LoanContactHistory
{
    public long Id { get; set; }
    public long BookLoanId { get; set; }
    public BookLoan? BookLoan { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [Required, MaxLength(1000)]
    public string Note { get; set; } = string.Empty;

    public int? ContactedByAdminAccountId { get; set; }
    public AdminAccount? ContactedByAdminAccount { get; set; }

    /// <summary>Tên/email tại thời điểm liên hệ, giữ nguyên khi tài khoản nhân sự đổi tên.</summary>
    [Required, MaxLength(256)]
    public string ContactedBy { get; set; } = string.Empty;
}
