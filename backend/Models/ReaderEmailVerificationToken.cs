using System.ComponentModel.DataAnnotations;

namespace Project.Models;

/// <summary>Liên kết xác nhận email bạn đọc: lưu dạng hash, dùng một lần, hết hạn sau 24 giờ.</summary>
public sealed class ReaderEmailVerificationToken
{
    public long Id { get; set; }
    public int ReaderAccountId { get; set; }
    public ReaderAccount ReaderAccount { get; set; } = null!;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    /// <summary>Có giá trị khi đây là liên kết xác nhận đổi email: địa chỉ mới sẽ thay email hiện tại.</summary>
    public string? NewEmail { get; set; }
}

public sealed class ResendEmailConfirmationViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [MaxLength(256)]
    [Display(Name = "Email đã đăng ký")]
    public string Email { get; set; } = string.Empty;
}
