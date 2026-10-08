using System.ComponentModel.DataAnnotations;

namespace Project.Models;

public sealed class ReaderProfileViewModel : IValidatableObject
{
    public string FullName { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public string StudentOrStaffCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? RejectionReason { get; set; }
    public string? CardCode { get; set; }
    public string? CardType { get; set; }
    public DateOnly? CardExpiresOn { get; set; }
    public string? CardStatus { get; set; }
    public LibraryCard? LibraryCard { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
    [MaxLength(20, ErrorMessage = "Số điện thoại không được vượt quá 20 ký tự.")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập địa chỉ.")]
    [MaxLength(500, ErrorMessage = "Địa chỉ không được vượt quá 500 ký tự.")]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [MaxLength(256, ErrorMessage = "Email không được vượt quá 256 ký tự.")]
    public string Email { get; set; } = string.Empty;
    /// <summary>Email mới đang chờ bạn đọc xác nhận (chưa có hiệu lực).</summary>
    public string? PendingEmail { get; set; }

    public string CurrentPassword { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(Email) &&
            !System.Text.RegularExpressions.Regex.IsMatch(Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            yield return new ValidationResult("Email must have a valid format.", [nameof(Email)]);
        }
    }
}
