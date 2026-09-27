using System.ComponentModel.DataAnnotations;

namespace Project.Models;

public sealed class ReaderChangePasswordViewModel : IValidatableObject
{
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu cũ.")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập lại mật khẩu mới.")]
    public string ConfirmNewPassword { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrEmpty(NewPassword) && !string.IsNullOrEmpty(ConfirmNewPassword) &&
            !string.Equals(NewPassword, ConfirmNewPassword, StringComparison.Ordinal))
        {
            yield return new ValidationResult(
                "Mật khẩu mới và mật khẩu xác nhận không trùng khớp.",
                [nameof(ConfirmNewPassword)]);
        }
    }
}
